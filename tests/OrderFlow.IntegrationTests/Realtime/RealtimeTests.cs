using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Api.Realtime;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Persistence;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Messaging;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.Worker.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace OrderFlow.IntegrationTests.Realtime;

[Collection("Realtime"), Trait("Category", "PostgreSQL"), Trait("Category", "RabbitMQ"), Trait("Category", "Redis")]
public sealed class RealtimeTests(PostgresServer server, RabbitMqServer broker, RedisServer redis) : PostgresTest(server)
{
    private readonly string prefix = "realtime-" + Guid.NewGuid().ToString("N");
    private readonly IDataProtectionProvider keys = new EphemeralDataProtectionProvider();
    private RabbitMqSettings Broker => broker.Settings(prefix);
    private Task<RealtimeApiHost> HostAsync(string name, bool dispatch = true, TimeSpan? expiry = null) =>
        RealtimeApiHost.StartAsync(Database, redis.ConnectionString, Broker, name, prefix, dispatch, keys, expiry);
    private async Task<OutboxMessage> NotificationAsync(HttpClient owner)
    {
        await using var setup = new AuthApiFactory(Database);
        var products = await CheckoutTestData.StockAsync(Database, setup, count: 1);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        await using var db = Database.CreateContext();
        var source = await db.OutboxMessages.AsNoTracking().SingleAsync(e => e.AggregateId == order.Id);
        await new NotificationProcessor(db, TimeProvider.System).ProcessAsync(OrderNotificationEvent.Parse(EventEnvelope.Serialize(source),
            source.EventId.ToString("D"), source.EventType, "application/json"), default);
        return await db.OutboxMessages.AsNoTracking().SingleAsync(e => e.EventType == "notifications.notification-created" && e.CausationId == source.EventId);
    }
    private async Task PublishOutboxAsync(RabbitMqEventPublisher publisher)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IEventPublisher>(publisher); services.AddSingleton<OutboxPublishingIteration>();
        services.AddDbContext<OrderFlowDbContext>(options => PersistenceRegistration.Configure(options, Database.ConnectionString));
        using var scopes = services.BuildServiceProvider(validateScopes: true);
        for (var i = 0; i < 20 && await scopes.GetRequiredService<OutboxPublishingIteration>().ProcessNextAsync(default); i++) { }
    }
    private static async Task<Task<byte[]>> ObserveAsync(IChannel channel, string queue)
    {
        var ready = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, e) => { ready.TrySetResult(e.Body.ToArray()); return Task.CompletedTask; };
        await channel.BasicConsumeAsync(queue, true, consumer); return ready.Task;
    }

    [Fact]
    public async Task Two_API_instances_Rabbit_on_A_reaches_owner_connections_on_B_through_real_Redis_with_minimal_duplicate_hints()
    {
        await using var a = await HostAsync("A"); await using var b = await HostAsync("B", dispatch: false);
        Assert.NotSame(a.App.Services.GetRequiredService<IHubContext<NotificationHub>>(), b.App.Services.GetRequiredService<IHubContext<NotificationHub>>());
        Assert.Contains("RedisHubLifetimeManager", b.App.Services.GetRequiredService<HubLifetimeManager<NotificationHub>>().GetType().Name, StringComparison.Ordinal);
        var user = await b.UserAsync(); var otherUser = await b.UserAsync();
        var (browser, cookies) = await b.LoginAsync(user.Email, user.Password); using var owner = browser;
        var (otherBrowser, otherCookies) = await b.LoginAsync(otherUser.Email, otherUser.Password); using var other = otherBrowser;
        await using var ownerSocket = await HintConnection.ConnectAsync(b.Address, owner, cookies);
        await using var otherSocket = await HintConnection.ConnectAsync(b.Address, other, otherCookies, "?userId=" + user.Id);
        await Assert.ThrowsAsync<HubException>(() => otherSocket.Connection.InvokeAsync("JoinOrderGroup", Guid.NewGuid().ToString("D")));
        await Assert.ThrowsAsync<HubException>(() => otherSocket.Connection.InvokeAsync("Subscribe", user.Id.ToString("D")));
        var notification = await NotificationAsync(owner); var barrier = await NotificationAsync(other);
        await using var publisher = new RabbitMqEventPublisher(Broker);
        await PublishOutboxAsync(publisher);
        var first = await ownerSocket.NextAsync(); var otherHint = await otherSocket.NextAsync();
        Assert.Equal(notification.EventId, first.GetProperty("eventId").GetGuid()); Assert.Equal(barrier.EventId, otherHint.GetProperty("eventId").GetGuid());
        Assert.Equal(new[] { "eventId", "notificationId", "orderId" }, first.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(notification.AggregateId, first.GetProperty("notificationId").GetGuid());
        Assert.False(otherSocket.Hints.Reader.TryRead(out _)); Assert.False(ownerSocket.Hints.Reader.TryRead(out _));
        await a.Logs.WaitAsync(2403); Assert.Contains(a.Logs.Messages, line => line.Contains("API A attempted hint", StringComparison.Ordinal));
        // Uncertain broker/Redis delivery may repeat. A duplicate carries exactly the same stable identities.
        await publisher.PublishAsync(notification, default); var duplicate = await ownerSocket.NextAsync(); Assert.Equal(first.GetRawText(), duplicate.GetRawText());
        await using var db = Database.CreateContext(); Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.Equal(2, await db.ProcessedMessages.CountAsync()); Assert.All(await db.Notifications.ToListAsync(), n => Assert.Null(n.ReadAt));
        var durable = await owner.GetFromJsonAsync<NotificationPage>("/api/notifications"); Assert.Equal(notification.AggregateId, Assert.Single(durable!.Items).Id);
    }

    [Fact]
    public async Task Unauthenticated_negotiate_CSRF_origin_and_transport_are_rejected_without_user_subscription_methods()
    {
        await using var host = await HostAsync("security", false); using var anonymous = host.Browser(new CookieContainer());
        using (var response = await anonymous.PostAsync(RealtimeRegistration.HubPath + "/negotiate?negotiateVersion=1", null)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using (var unauthenticated = new HubConnectionBuilder().WithUrl(new Uri(host.Address, RealtimeRegistration.HubPath)).Build())
            await Assert.ThrowsAsync<HttpRequestException>(() => unauthenticated.StartAsync());
        var user = await host.UserAsync(); var (browser, cookies) = await host.LoginAsync(user.Email, user.Password); using var owner = browser;
        using (var response = await owner.PostAsync(RealtimeRegistration.HubPath + "/negotiate?negotiateVersion=1", null)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var token = await BrowserRequests.TokenAsync(owner);
        using var request = new HttpRequestMessage(HttpMethod.Post, RealtimeRegistration.HubPath + "/negotiate?negotiateVersion=1");
        request.Headers.Add("X-CSRF-TOKEN", token); request.Headers.Add("Origin", "https://untrusted.example.invalid");
        using (var response = await owner.SendAsync(request)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var accessToken = new HttpRequestMessage(HttpMethod.Post, RealtimeRegistration.HubPath + "/negotiate?access_token=untrusted&negotiateVersion=1");
        accessToken.Headers.Add("X-CSRF-TOKEN", token);
        using (var response = await owner.SendAsync(accessToken)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var validNegotiate = new HttpRequestMessage(HttpMethod.Post, RealtimeRegistration.HubPath + "/negotiate?negotiateVersion=1");
        validNegotiate.Headers.Add("X-CSRF-TOKEN", token);
        using var validResponse = await owner.SendAsync(validNegotiate); Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        using var negotiation = JsonDocument.Parse(await validResponse.Content.ReadAsStringAsync());
        Assert.Equal("WebSockets", Assert.Single(negotiation.RootElement.GetProperty("availableTransports").EnumerateArray()).GetProperty("transport").GetString());
        await using var connection = await HintConnection.ConnectAsync(host.Address, owner, cookies);
        Assert.Equal(HubConnectionState.Connected, connection.Connection.State);
    }

    [Theory]
    [InlineData("recipient")]
    [InlineData("schema")]
    [InlineData("malformed")]
    [InlineData("wrong-kind")]
    public async Task Bad_hint_correlation_and_unsupported_payload_are_parked_without_changing_durable_notifications(string fault)
    {
        await using var host = await HostAsync("poison"); var user = await host.UserAsync();
        var (browser, cookies) = await host.LoginAsync(user.Email, user.Password); using var owner = browser;
        await using var socket = await HintConnection.ConnectAsync(host.Address, owner, cookies);
        var source = await NotificationAsync(owner); var body = EventEnvelope.Serialize(source);
        if (fault == "recipient") body = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace(user.Id.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal));
        if (fault == "schema") body = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal));
        if (fault == "malformed") body = "{"u8.ToArray();
        if (fault == "wrong-kind")
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(body)!; node["data"]!["createdAt"] = 42;
            body = Encoding.UTF8.GetBytes(node.ToJsonString());
        }
        await using var connection = await Broker.Factory().CreateConnectionAsync(); await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
        await RabbitMqTopology.DeclareAsync(channel, Broker, default); var dead = await ObserveAsync(channel, Broker.RealtimeDeadQueue);
        await channel.BasicPublishAsync(Broker.Exchange, source.EventType, true,
            new BasicProperties { Persistent = true, MessageId = source.EventId.ToString("D"), Type = source.EventType, ContentType = "application/json" }, body);
        Assert.Equal(body, await dead.WaitAsync(TimeSpan.FromSeconds(15))); Assert.False(socket.Hints.Reader.TryRead(out _));
        Assert.Single((await owner.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.Items);
    }

    [Fact]
    public async Task Redis_failure_logs_bounded_retry_then_DLQ_but_durable_HTTP_state_remains_available()
    {
        await using var host = await HostAsync("redis-down"); var user = await host.UserAsync();
        var (browser, cookies) = await host.LoginAsync(user.Email, user.Password); using var owner = browser;
        await using var socket = await HintConnection.ConnectAsync(host.Address, owner, cookies);
        var source = await NotificationAsync(owner);
        try
        {
            await redis.StopAsync(); await host.Logs.WaitAsync(2405);
            await using var connection = await Broker.Factory().CreateConnectionAsync(); await using var channel = await connection.CreateChannelAsync();
            await RabbitMqTopology.DeclareAsync(channel, Broker, default); var dead = await ObserveAsync(channel, Broker.RealtimeDeadQueue);
            await using var publisher = new RabbitMqEventPublisher(Broker); await publisher.PublishAsync(source, default);
            await dead.WaitAsync(TimeSpan.FromSeconds(20)); await host.Logs.WaitAsync(2402);
            Assert.Single((await owner.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.Items);
            Assert.False(socket.Hints.Reader.TryRead(out _));
        }
        finally { await redis.StartAsync(); }
    }

    [Fact]
    public async Task API_shutdown_aborts_authenticated_connections()
    {
        await using var host = await HostAsync("shutdown", false); var user = await host.UserAsync();
        var (browser, cookies) = await host.LoginAsync(user.Email, user.Password); using var owner = browser;
        await using var socket = await HintConnection.ConnectAsync(host.Address, owner, cookies);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await host.App.StopAsync(budget.Token);
        await socket.Closed.Task.WaitAsync(budget.Token); Assert.Equal(HubConnectionState.Disconnected, socket.Connection.State);
    }

    [Fact]
    public async Task Authentication_expiry_closes_socket_and_reconnect_refetches_notifications_instead_of_replaying_hints()
    {
        await using var host = await HostAsync("expiry", false, TimeSpan.FromSeconds(3)); var user = await host.UserAsync();
        var (browser, cookies) = await host.LoginAsync(user.Email, user.Password); using var owner = browser;
        await using var socket = await HintConnection.ConnectAsync(host.Address, owner, cookies);
        await socket.Closed.Task.WaitAsync(TimeSpan.FromSeconds(12)); Assert.Equal(HubConnectionState.Disconnected, socket.Connection.State);
        var (newBrowser, newCookies) = await host.LoginAsync(user.Email, user.Password); using var refreshed = newBrowser;
        var source = await NotificationAsync(refreshed);
        await using var reconnected = await HintConnection.ConnectAsync(host.Address, refreshed, newCookies);
        var durable = (await refreshed.GetFromJsonAsync<NotificationPage>("/api/notifications"))!;
        Assert.Equal(source.AggregateId, Assert.Single(durable.Items).Id); Assert.False(reconnected.Hints.Reader.TryRead(out _));
    }
}

internal sealed class HintConnection : IAsyncDisposable
{
    public HubConnection Connection { get; }
    public System.Threading.Channels.Channel<JsonElement> Hints { get; } = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HintConnection(HubConnection connection)
    {
        Connection = connection; connection.On<JsonElement>("NotificationAvailable", hint => Hints.Writer.TryWrite(hint.Clone()));
        connection.On("ConnectionReady", () => Ready.TrySetResult()); connection.Closed += _ => { Closed.TrySetResult(); return Task.CompletedTask; };
    }
    public static async Task<HintConnection> ConnectAsync(Uri address, HttpClient browser, CookieContainer cookies, string query = "")
    {
        var csrf = await BrowserRequests.TokenAsync(browser);
        var connection = new HubConnectionBuilder().WithUrl(new Uri(address, RealtimeRegistration.HubPath + query), options =>
        {
            options.Cookies = cookies; options.Headers["X-CSRF-TOKEN"] = csrf; options.Headers["Origin"] = address.GetLeftPart(UriPartial.Authority);
            options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
        }).Build();
        var result = new HintConnection(connection);
        try { using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await connection.StartAsync(budget.Token); await result.Ready.Task.WaitAsync(budget.Token); return result; }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async Task<JsonElement> NextAsync() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); return await Hints.Reader.ReadAsync(timeout.Token); }
    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
