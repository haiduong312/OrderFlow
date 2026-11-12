using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.Worker.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace OrderFlow.IntegrationTests.Messaging;

[Collection("Messaging"), Trait("Category", "PostgreSQL"), Trait("Category", "RabbitMQ")]
public sealed class OutboxPublishingTests(PostgresServer server, RabbitMqServer broker) : PostgresTest(server)
{
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly string prefix = "test-" + Guid.NewGuid().ToString("N");
    private ServiceProvider Services(IEventPublisher publisher, DbCommandInterceptor? gate = null)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(publisher);
        services.AddDbContext<OrderFlowDbContext>(options =>
        {
            PersistenceRegistration.Configure(options, Database.ConnectionString);
            if (gate is not null) options.AddInterceptors(gate);
        });
        services.AddSingleton<OutboxPublishingIteration>(); return services.BuildServiceProvider(validateScopes: true);
    }
    private async Task<bool> PublishAsync(IEventPublisher publisher, DbCommandInterceptor? gate = null)
    {
        using var services = Services(publisher, gate);
        return await services.GetRequiredService<OutboxPublishingIteration>().ProcessNextAsync(default);
    }
    private async Task<OutboxMessage> SeedAsync()
    {
        var message = Message(); await using var db = Database.CreateContext(); db.OutboxMessages.Add(message);
        await db.SaveChangesAsync(); return message;
    }
    private OutboxMessage Message() => new()
    {
        EventId = Guid.NewGuid(), EventType = "ordering.order-created", SchemaVersion = 1, Producer = "Ordering",
        AggregateType = "Order", AggregateId = Guid.NewGuid(), AggregateVersion = 1, CorrelationId = Guid.NewGuid(),
        CausationId = Guid.NewGuid(), Payload = "{\"total\":\"25.00\",\"currency\":\"USD\"}",
        OccurredAt = clock.GetUtcNow(), NextAttemptAt = clock.GetUtcNow()
    };
    private async Task<OutboxMessage> ReadAsync()
    { await using var db = Database.CreateContext(); return await db.OutboxMessages.AsNoTracking().SingleAsync(); }
    private sealed record ReceivedMessage(byte[] Body, IReadOnlyBasicProperties BasicProperties);
    private async Task<ReceivedMessage> GetAsync(RabbitMqSettings settings, string? queue = null)
    {
        await using var connection = await settings.Factory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var result = await channel.BasicGetAsync(queue ?? settings.Queue, autoAck: true);
        Assert.NotNull(result); return new(result.Body.ToArray(), result.BasicProperties);
    }

    [Fact]
    public async Task Confirmed_mandatory_publish_is_persistent_versioned_and_does_not_imply_consumer_completion()
    {
        using var trace = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        trace.TraceStateString = "orderflow=test";
        var message = await SeedAsync(); var settings = broker.Settings(prefix);
        await using var publisher = new RabbitMqEventPublisher(settings);
        var probe = new TransactionProbe(publisher, Database);
        Assert.True(await PublishAsync(probe)); Assert.False(await PublishAsync(probe)); Assert.Equal(1, probe.Calls);
        var saved = await ReadAsync(); Assert.Equal(OutboxStatus.Published, saved.Status); Assert.Equal(1, saved.AttemptCount);
        Assert.Null(saved.LeaseOwner); Assert.Null(saved.LeaseToken); Assert.NotNull(saved.PublishedAt);
        var received = await GetAsync(settings); Assert.True(received.BasicProperties.Persistent);
        Assert.Equal(message.EventId.ToString("D"), received.BasicProperties.MessageId);
        Assert.Equal("application/json", received.BasicProperties.ContentType);
        using var body = JsonDocument.Parse(received.Body);
        Assert.Equal(1, body.RootElement.GetProperty("envelopeVersion").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(message.EventId, body.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(message.AggregateId, body.RootElement.GetProperty("aggregateId").GetGuid());
        Assert.Equal(message.CorrelationId, body.RootElement.GetProperty("correlationId").GetGuid());
        Assert.Equal(message.TraceParent, body.RootElement.GetProperty("traceParent").GetString());
        Assert.Equal("25.00", body.RootElement.GetProperty("data").GetProperty("total").GetString());
        await using var db = Database.CreateContext(); Assert.Empty(await db.ProcessedMessages.ToListAsync());
    }

    [Fact]
    public async Task Checkout_succeeds_while_broker_unavailable_and_committed_event_publishes_after_recovery()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        await using var broken = new RabbitMqEventPublisher(broker.Settings(prefix, port: port, timeout: 1));
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var browser = owner;
        var products = await CheckoutTestData.StockAsync(Database, host, count: 1);
        var order = await OrderManagementTestData.CreateAsync(browser, CheckoutTestData.Body(products));
        Assert.True(await PublishAsync(broken));
        var pending = await ReadAsync(); Assert.Equal(OutboxStatus.Pending, pending.Status); Assert.Null(pending.PublishedAt);
        Assert.Equal("broker_unavailable", pending.LastErrorCode); Assert.NotNull(pending.LastFailureAt);
        await using (var db = Database.CreateContext()) Assert.Equal(order.Id, (await db.Orders.SingleAsync()).Id);
        clock.Advance(TimeSpan.FromSeconds(5)); var settings = broker.Settings(prefix);
        await using var recovered = new RabbitMqEventPublisher(settings);
        Assert.True(await PublishAsync(recovered)); Assert.Equal(OutboxStatus.Published, (await ReadAsync()).Status);
        Assert.Equal(pending.EventId.ToString("D"), (await GetAsync(settings)).BasicProperties.MessageId);
    }

    [Fact]
    public async Task Mandatory_return_is_not_mistaken_for_broker_ACK_and_retries_after_binding_repair()
    {
        await SeedAsync(); var settings = broker.Settings(prefix); await using var publisher = new RabbitMqEventPublisher(settings);
        await publisher.EnsureTopologyAsync(default);
        await using (var connection = await settings.Factory().CreateConnectionAsync())
        await using (var admin = await connection.CreateChannelAsync())
        {
            await admin.QueueUnbindAsync(settings.Queue, settings.Exchange, "#");
            foreach (var type in OrderFlow.Application.Modules.Notifications.OrderNotificationEvent.SupportedTypes.Keys)
                await admin.QueueUnbindAsync(settings.NotificationQueue, settings.Exchange, type);
        }
        Assert.True(await PublishAsync(publisher));
        var failed = await ReadAsync(); Assert.Equal(OutboxStatus.Pending, failed.Status); Assert.Equal("broker_unroutable", failed.LastErrorCode);
        Assert.Null(failed.PublishedAt);
        clock.Advance(TimeSpan.FromSeconds(5)); // reconnect redeclares the required durable binding
        Assert.True(await PublishAsync(publisher)); Assert.Equal(OutboxStatus.Published, (await ReadAsync()).Status);
        Assert.NotNull(await GetAsync(settings));
    }

    [Fact]
    public async Task Broker_NACK_on_full_durable_queue_cannot_mark_Published()
    {
        await SeedAsync(); var settings = broker.Settings(prefix, limit: 1); await using var publisher = new RabbitMqEventPublisher(settings);
        // Quorum queues may overshoot max-length slightly. Observe an actual NACK rather than
        // assuming that a particular publish number must fail.
        var saturated = false;
        for (var i = 0; i < 10 && !saturated; i++)
        {
            try { await publisher.PublishAsync(Message(), default); }
            catch (EventPublishException failure) when (failure.Code == "broker_nack") { saturated = true; }
        }
        Assert.True(saturated);
        Assert.True(await PublishAsync(publisher));
        var pending = await ReadAsync(); Assert.Equal(OutboxStatus.Pending, pending.Status); Assert.Equal("broker_nack", pending.LastErrorCode);
        Assert.Null(pending.PublishedAt);
        await using (var connection = await settings.Factory().CreateConnectionAsync())
        await using (var admin = await connection.CreateChannelAsync())
        {
            foreach (var queue in new[] { settings.Queue, settings.NotificationQueue })
                for (var i = 0; i < 10; i++)
                    if (await admin.BasicGetAsync(queue, true) is null) break;
        }
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await PublishAsync(publisher)); Assert.Equal(OutboxStatus.Published, (await ReadAsync()).Status);
    }

    [Fact]
    public async Task Dropped_real_broker_confirm_times_out_and_republication_delivers_same_event_twice()
    {
        var message = await SeedAsync(); var real = broker.Settings(prefix);
        await using var proxy = new ConfirmDroppingProxy(real.HostName, real.Port);
        await using (var uncertain = new RabbitMqEventPublisher(broker.Settings(prefix, proxy.Port, timeout: 1)))
        {
            using var setupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await uncertain.EnsureTopologyAsync(setupBudget.Token);
            Assert.True(await PublishAsync(uncertain));
            await proxy.ConfirmDropped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var pending = await ReadAsync(); Assert.Equal(OutboxStatus.Pending, pending.Status);
        Assert.Equal("broker_confirm_timeout", pending.LastErrorCode); Assert.Null(pending.PublishedAt);
        clock.Advance(TimeSpan.FromSeconds(5)); await using var retry = new RabbitMqEventPublisher(real);
        Assert.True(await PublishAsync(retry));
        var one = await GetAsync(real); var two = await GetAsync(real);
        Assert.Equal(message.EventId.ToString("D"), one.BasicProperties.MessageId);
        Assert.Equal(one.BasicProperties.MessageId, two.BasicProperties.MessageId); Assert.Equal(one.Body.ToArray(), two.Body.ToArray());
        Assert.Equal(2, (await ReadAsync()).AttemptCount);
    }

    [Fact]
    public async Task Crash_after_confirm_before_SQL_ack_reclaims_expired_lease_and_can_deliver_duplicate()
    {
        await SeedAsync(); var settings = broker.Settings(prefix); await using var publisher = new RabbitMqEventPublisher(settings);
        await using var setup = Database.CreateContext();
        await setup.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION messaging.reject_outbox_ack_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."Status"='Published' THEN RAISE EXCEPTION 'Injected local acknowledgement failure'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER "TR_TestOutboxAck" BEFORE UPDATE ON messaging."OutboxMessages" FOR EACH ROW EXECUTE FUNCTION messaging.reject_outbox_ack_test();
            """);
        try { await Assert.ThrowsAsync<PostgresException>(() => PublishAsync(publisher)); }
        finally { await setup.Database.ExecuteSqlRawAsync("DROP TRIGGER \"TR_TestOutboxAck\" ON messaging.\"OutboxMessages\"; DROP FUNCTION messaging.reject_outbox_ack_test();"); }
        var flight = await ReadAsync(); Assert.Equal(OutboxStatus.InFlight, flight.Status); Assert.NotNull(flight.LeaseToken);
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(await PublishAsync(publisher)); Assert.Equal(OutboxStatus.Published, (await ReadAsync()).Status);
        Assert.Equal((await GetAsync(settings)).BasicProperties.MessageId, (await GetAsync(settings)).BasicProperties.MessageId);
    }

    [Fact]
    public async Task Independent_workers_contend_before_claim_and_only_one_can_publish()
    {
        await SeedAsync(); var settings = broker.Settings(prefix);
        await using var first = new RabbitMqEventPublisher(settings); await using var second = new RabbitMqEventPublisher(settings);
        var gate = new CommandRaceGate(sql => sql.Contains("SELECT * FROM messaging.\"OutboxMessages\"", StringComparison.Ordinal)
            && sql.Contains("SKIP LOCKED", StringComparison.Ordinal)) { Enabled = true };
        var results = await Task.WhenAll(PublishAsync(first, gate), PublishAsync(second, gate));
        Assert.Equal(2, gate.Participants); Assert.Single(results, r => r); Assert.Equal(1, (await ReadAsync()).AttemptCount);
        await GetAsync(settings);
        await using var connection = await settings.Factory().CreateConnectionAsync(); await using var channel = await connection.CreateChannelAsync();
        Assert.Null(await channel.BasicGetAsync(settings.Queue, true));
    }

    [Fact]
    public async Task Stale_owner_cannot_complete_after_reclaim_and_newer_publication()
    {
        await SeedAsync(); var settings = broker.Settings(prefix);
        await using var first = new RabbitMqEventPublisher(settings); await using var second = new RabbitMqEventPublisher(settings);
        var pause = new PauseAfterPublish(first); var old = PublishAsync(pause);
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); clock.Advance(TimeSpan.FromSeconds(60));
            Assert.True(await PublishAsync(second));
        }
        finally { pause.Release.TrySetResult(); }
        Assert.True(await old); var saved = await ReadAsync(); Assert.Equal(OutboxStatus.Published, saved.Status);
        Assert.Equal(2, saved.AttemptCount); Assert.Equal(clock.GetUtcNow(), saved.PublishedAt);
        Assert.Null(saved.LeaseToken);
    }

    [Fact]
    public async Task Bounded_failures_and_crash_at_eighth_claim_remain_visible_without_immediate_retry()
    {
        await SeedAsync(); var failure = new FailingPublisher();
        for (var i = 1; i <= 7; i++)
        {
            Assert.True(await PublishAsync(failure)); Assert.False(await PublishAsync(failure));
            var saved = await ReadAsync(); Assert.Equal(i, saved.AttemptCount); Assert.Equal(OutboxStatus.Pending, saved.Status);
            clock.Advance(saved.NextAttemptAt - clock.GetUtcNow());
        }
        var crash = new CrashPublisher(); await Assert.ThrowsAsync<InvalidOperationException>(() => PublishAsync(crash));
        Assert.Equal(OutboxStatus.InFlight, (await ReadAsync()).Status); clock.Advance(TimeSpan.FromSeconds(60));
        Assert.False(await PublishAsync(failure));
        var exhausted = await ReadAsync(); Assert.Equal(8, exhausted.AttemptCount); Assert.Equal(OutboxStatus.Failed, exhausted.Status);
        Assert.Equal("publisher_attempts_exhausted", exhausted.LastErrorCode); Assert.Null(exhausted.LeaseToken);
    }

    [Fact]
    public async Task Finite_durable_dead_letter_topology_delays_once_then_parks_without_a_requeue_cycle()
    {
        var settings = broker.Settings(prefix); await using var publisher = new RabbitMqEventPublisher(settings);
        var message = Message(); await publisher.PublishAsync(message, default);
        await using var connection = await settings.Factory().CreateConnectionAsync(); await using var channel = await connection.CreateChannelAsync();
        var retry = new TaskCompletionSource<(ulong Tag, byte[] Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dead = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryConsumer = new AsyncEventingBasicConsumer(channel);
        retryConsumer.ReceivedAsync += (_, delivery) => { retry.TrySetResult((delivery.DeliveryTag, delivery.Body.ToArray())); return Task.CompletedTask; };
        var deadConsumer = new AsyncEventingBasicConsumer(channel);
        deadConsumer.ReceivedAsync += (_, delivery) => { dead.TrySetResult(delivery.Body.ToArray()); return Task.CompletedTask; };
        await channel.BasicConsumeAsync(settings.RetryQueue, false, retryConsumer);
        await channel.BasicConsumeAsync(settings.DeadQueue, true, deadConsumer);
        var original = await channel.BasicGetAsync(settings.Queue, false); Assert.NotNull(original);
        await channel.BasicNackAsync(original.DeliveryTag, false, requeue: false);
        var delayed = await retry.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await channel.BasicNackAsync(delayed.Tag, false, requeue: false);
        var parked = await dead.Task.WaitAsync(TimeSpan.FromSeconds(15));
        using var document = JsonDocument.Parse(parked); Assert.Equal(message.EventId, document.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(original.Body.ToArray(), delayed.Body); Assert.Equal(delayed.Body, parked);
    }

    [Fact]
    public async Task Durable_persistent_queued_event_survives_real_broker_restart()
    {
        await SeedAsync(); var settings = broker.Settings(prefix);
        await using (var publisher = new RabbitMqEventPublisher(settings)) Assert.True(await PublishAsync(publisher));
        await broker.RestartAsync();
        Assert.Equal((await ReadAsync()).EventId.ToString("D"), (await GetAsync(broker.Settings(prefix))).BasicProperties.MessageId);
    }

    [Fact]
    public async Task Uncommitted_intention_is_invisible_to_publisher_and_rollback_leaves_no_event()
    {
        await using var db = Database.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.OutboxMessages.Add(Message()); await db.SaveChangesAsync();
        Assert.False(await PublishAsync(new CrashPublisher())); // Would throw if network delivery was attempted.
        await transaction.RollbackAsync();
        await using var read = Database.CreateContext(); Assert.Empty(await read.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task SQL_preserves_envelope_and_terminal_Published_facts()
    {
        await SeedAsync(); await using var publisher = new RabbitMqEventPublisher(broker.Settings(prefix));
        Assert.True(await PublishAsync(publisher));
        foreach (var sql in new[] { "UPDATE messaging.\"OutboxMessages\" SET \"Payload\"='{{}}'::jsonb",
            "UPDATE messaging.\"OutboxMessages\" SET \"Status\"='Pending', \"PublishedAt\"=NULL",
            "DELETE FROM messaging.\"OutboxMessages\"" })
        {
            await using var db = Database.CreateContext();
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        }
        Assert.Equal(OutboxStatus.Published, (await ReadAsync()).Status);
    }

    [Fact]
    public async Task Existing_pending_outbox_survives_additive_upgrade_and_can_be_published()
    {
        await using var legacy = await Server.CreateDatabaseAsync("20261111044314_AddRefundRecovery");
        var message = Message();
        await using (var db = legacy.CreateContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO messaging."OutboxMessages"
                    ("EventId","EventType","SchemaVersion","Producer","AggregateType","AggregateId","AggregateVersion",
                     "CorrelationId","CausationId","Payload","OccurredAt","Status","AttemptCount","NextAttemptAt")
                VALUES ({message.EventId},{message.EventType},1,'Ordering','Order',{message.AggregateId},1,
                    {message.CorrelationId},{message.CausationId},CAST({message.Payload} AS jsonb),{message.OccurredAt},'Pending',0,{message.NextAttemptAt})
                """);
            await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        }
        await using var publisher = new RabbitMqEventPublisher(broker.Settings(prefix));
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IEventPublisher>(publisher);
        services.AddDbContext<OrderFlowDbContext>(options => PersistenceRegistration.Configure(options, legacy.ConnectionString));
        services.AddSingleton<OutboxPublishingIteration>(); using var provider = services.BuildServiceProvider(validateScopes: true);
        Assert.True(await provider.GetRequiredService<OutboxPublishingIteration>().ProcessNextAsync(default));
        await using var read = legacy.CreateContext(); var saved = await read.OutboxMessages.SingleAsync();
        Assert.Equal(message.EventId, saved.EventId); Assert.Equal(OutboxStatus.Published, saved.Status); Assert.Null(saved.TraceParent);
        Assert.Equal(message.EventId.ToString("D"), (await GetAsync(broker.Settings(prefix))).BasicProperties.MessageId);
    }

    [Fact]
    public async Task One_publisher_serializes_concurrent_channel_use_without_corrupting_frames()
    {
        var settings = broker.Settings(prefix); await using var publisher = new RabbitMqEventPublisher(settings);
        var messages = Enumerable.Range(0, 12).Select(_ => Message()).ToArray();
        await Task.WhenAll(messages.Select(m => publisher.PublishAsync(m, default)));
        var delivered = new List<string?>();
        for (var i = 0; i < messages.Length; i++) delivered.Add((await GetAsync(settings)).BasicProperties.MessageId);
        Assert.Equal(messages.Select(m => m.EventId.ToString("D")).Order(), delivered.Order());
    }

    [Fact]
    public async Task Shutdown_during_confirm_wait_leaves_lease_for_restart_instead_of_false_Published()
    {
        await SeedAsync(); var real = broker.Settings(prefix);
        await using var proxy = new ConfirmDroppingProxy(real.HostName, real.Port);
        await using var publisher = new RabbitMqEventPublisher(broker.Settings(prefix, proxy.Port, timeout: 5));
        using var setupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await publisher.EnsureTopologyAsync(setupBudget.Token);
        using var services = Services(publisher); using var shutdown = new CancellationTokenSource();
        var pending = services.GetRequiredService<OutboxPublishingIteration>().ProcessNextAsync(shutdown.Token);
        await proxy.ConfirmDropped.Task.WaitAsync(TimeSpan.FromSeconds(10)); shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var saved = await ReadAsync(); Assert.Equal(OutboxStatus.InFlight, saved.Status); Assert.Null(saved.PublishedAt);
        clock.Advance(TimeSpan.FromSeconds(60)); await using var restarted = new RabbitMqEventPublisher(real);
        Assert.True(await PublishAsync(restarted)); Assert.Equal(OutboxStatus.Published, (await ReadAsync()).Status);
    }
}

internal sealed class TransactionProbe(IEventPublisher inner, PostgresDatabase database) : IEventPublisher
{
    public int Calls { get; private set; }
    public async Task PublishAsync(OutboxMessage message, CancellationToken token)
    {
        Calls++;
        await using (var db = database.CreateContext())
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            var rows = await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM messaging.\"OutboxMessages\" WHERE \"EventId\"={message.EventId} FOR UPDATE NOWAIT").ToListAsync(token);
            Assert.Equal(OutboxStatus.InFlight, Assert.Single(rows).Status); await transaction.RollbackAsync(token);
        }
        await inner.PublishAsync(message, token);
    }
}
internal sealed class PauseAfterPublish(IEventPublisher inner) : IEventPublisher
{
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task PublishAsync(OutboxMessage message, CancellationToken token)
    { await inner.PublishAsync(message, token); Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), token); }
}
internal sealed class FailingPublisher : IEventPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken token) => throw new EventPublishException("broker_unavailable", true, true);
}
internal sealed class CrashPublisher : IEventPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken token) => throw new InvalidOperationException("Injected crash before result acknowledgement");
}
