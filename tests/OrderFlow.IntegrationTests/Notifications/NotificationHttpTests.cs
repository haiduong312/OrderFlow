using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Notifications;

[Collection("PostgreSQL"), Trait("Category", "PostgreSQL")]
public sealed class NotificationHttpTests(PostgresServer server) : PostgresTest(server)
{
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private async Task<Guid> CreateNotificationAsync(AuthApiFactory host, HttpClient owner)
    {
        var products = await CheckoutTestData.StockAsync(Database, host, count: 1);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        await using var db = Database.CreateContext();
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == order.Id);
        await new NotificationProcessor(db, clock).ProcessAsync(OrderNotificationEvent.Parse(EventEnvelope.Serialize(message),
            message.EventId.ToString("D"), message.EventType, "application/json"), default);
        return (await db.Notifications.SingleAsync(n => n.OrderId == order.Id)).Id;
    }
    private static Task<HttpResponseMessage> ReadAsync(HttpClient browser, Guid id, string? csrf) =>
        BrowserRequests.SendAsync(browser, HttpMethod.Post, $"/api/notifications/{id}/read", new { }, csrf);

    [Fact]
    public async Task Anonymous_cannot_list_get_count_or_mark_read()
    {
        await using var host = new AuthApiFactory(Database, clock: clock); using var browser = host.Browser();
        foreach (var path in new[] { "/api/notifications", "/api/notifications/unread-count", "/api/notifications/" + Guid.NewGuid() })
        { using var response = await browser.GetAsync(path); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
        using var read = await ReadAsync(browser, Guid.NewGuid(), await BrowserRequests.TokenAsync(browser));
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
    }

    [Fact]
    public async Task List_unread_detail_and_read_are_scoped_to_current_user_even_for_admin_or_spoofed_query()
    {
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (ownerClient, ownerId) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var owner = ownerClient;
        var (otherClient, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var other = otherClient;
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(host, "Admin"); using var admin = adminClient;
        var id = await CreateNotificationAsync(host, owner); var otherId = await CreateNotificationAsync(host, other);
        using var list = await owner.GetAsync("/api/notifications?pageSize=1"); Assert.True(list.Headers.CacheControl?.NoStore);
        var page = (await list.Content.ReadFromJsonAsync<NotificationPage>())!;
        Assert.Equal(1, page.TotalCount); Assert.Equal(id, Assert.Single(page.Items).Id);
        Assert.Equal(1, (await owner.GetFromJsonAsync<UnreadNotificationCount>("/api/notifications/unread-count"))!.Count);
        var spoof = (await other.GetFromJsonAsync<NotificationPage>($"/api/notifications?userId={ownerId}&recipientUserId={ownerId}"))!;
        Assert.Equal(otherId, Assert.Single(spoof.Items).Id);
        foreach (var browser in new[] { other, admin })
        {
            using var detail = await browser.GetAsync("/api/notifications/" + id); Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
            using var read = await ReadAsync(browser, id, await BrowserRequests.TokenAsync(browser)); Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        }
        Assert.Empty((await admin.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.Items);
        using (var noCsrf = await ReadAsync(owner, id, null)) Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using (var spoofBody = await BrowserRequests.SendAsync(owner, HttpMethod.Post, $"/api/notifications/{id}/read",
            new { recipientUserId = ownerId }, await BrowserRequests.TokenAsync(owner)))
            Assert.Equal(HttpStatusCode.BadRequest, spoofBody.StatusCode);
        Assert.Null((await owner.GetFromJsonAsync<NotificationResponse>("/api/notifications/" + id))!.ReadAt);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var success = await ReadAsync(owner, id, await BrowserRequests.TokenAsync(owner)); Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var readAt = (await success.Content.ReadFromJsonAsync<NotificationResponse>())!.ReadAt; Assert.Equal(clock.GetUtcNow(), readAt);
        clock.Advance(TimeSpan.FromSeconds(5));
        using var replay = await ReadAsync(owner, id, await BrowserRequests.TokenAsync(owner));
        Assert.Equal(readAt, (await replay.Content.ReadFromJsonAsync<NotificationResponse>())!.ReadAt);
        Assert.Equal(0, (await owner.GetFromJsonAsync<UnreadNotificationCount>("/api/notifications/unread-count"))!.Count);
        Assert.Empty((await owner.GetFromJsonAsync<NotificationPage>("/api/notifications?unreadOnly=true"))!.Items);
        Assert.Equal(1, (await other.GetFromJsonAsync<UnreadNotificationCount>("/api/notifications/unread-count"))!.Count);
        await using (var db = Database.CreateContext())
        {
            var sourceId = (await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id)).SourceEventId;
            var source = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventId == sourceId);
            await new NotificationProcessor(db, clock).ProcessAsync(OrderNotificationEvent.Parse(EventEnvelope.Serialize(source),
                source.EventId.ToString("D"), source.EventType, "application/json"), default);
        }
        Assert.Equal(readAt, (await owner.GetFromJsonAsync<NotificationResponse>("/api/notifications/" + id))!.ReadAt);
        using var missing = await ReadAsync(owner, Guid.NewGuid(), await BrowserRequests.TokenAsync(owner)); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("unreadOnly=wrong")]
    public async Task Invalid_collection_parameters_are_rejected(string query)
    {
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (browser, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var owner = browser;
        using var response = await owner.GetAsync("/api/notifications?" + query); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Simultaneous_read_receipts_are_idempotent_with_no_order_or_stock_changes()
    {
        var gate = new ReadReceiptGate();
        await using var host = new AuthApiFactory(Database, clock: clock, catalogInterceptor: gate);
        var (browser, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var owner = browser;
        var id = await CreateNotificationAsync(host, owner); var csrf = await BrowserRequests.TokenAsync(owner);
        gate.Enabled = true; clock.Advance(TimeSpan.FromSeconds(1));
        var responses = await Task.WhenAll(ReadAsync(owner, id, csrf), ReadAsync(owner, id, csrf));
        try
        {
            Assert.Equal(2, gate.Participants);
            foreach (var response in responses) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(clock.GetUtcNow(), (await response.Content.ReadFromJsonAsync<NotificationResponse>())!.ReadAt); }
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var db = Database.CreateContext(); Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(2, await db.OutboxMessages.CountAsync()); Assert.Single(await db.OrderStatusHistory.ToListAsync());
        Assert.Equal(1, (await db.Orders.SingleAsync()).Version);
    }

    private sealed class ReadReceiptGate : DbCommandInterceptor
    {
        private readonly RaceRendezvous rendezvous = new();
        public bool Enabled { get; set; }
        public int Participants => rendezvous.Participants;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Enabled && command.CommandText.StartsWith("UPDATE notifications.", StringComparison.Ordinal))
                await rendezvous.ArriveAsync(data.Context!.ContextId.InstanceId, token);
            return result;
        }
    }
}
