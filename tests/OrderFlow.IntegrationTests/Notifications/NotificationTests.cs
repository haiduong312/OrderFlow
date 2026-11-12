using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.IntegrationTests.Messaging;
using OrderFlow.Worker.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace OrderFlow.IntegrationTests.Notifications;

[Collection("Messaging"), Trait("Category", "PostgreSQL"), Trait("Category", "RabbitMQ")]
public sealed class NotificationTests(PostgresServer server, RabbitMqServer broker) : PostgresTest(server)
{
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly string prefix = "notifications-" + Guid.NewGuid().ToString("N");
    private ServiceProvider Services(DbCommandInterceptor? interceptor = null)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<NotificationProcessor>();
        services.AddDbContext<OrderFlowDbContext>(options =>
        {
            PersistenceRegistration.Configure(options, Database.ConnectionString);
            if (interceptor is not null) options.AddInterceptors(interceptor);
        });
        return services.BuildServiceProvider(validateScopes: true);
    }
    private static NotificationDeliveryHandler Handler(ServiceProvider services, DeliveryLog<NotificationDeliveryHandler> log) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), log);
    private async Task<OutboxMessage> CreateAsync()
    {
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (owner, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var browser = owner;
        var products = await CheckoutTestData.StockAsync(Database, host, count: 1);
        var order = await OrderManagementTestData.CreateAsync(browser, CheckoutTestData.Body(products));
        await using var db = Database.CreateContext();
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == order.Id);
    }
    private async Task ProcessAsync(OutboxMessage message)
    {
        var bytes = EventEnvelope.Serialize(message);
        await using var db = Database.CreateContext();
        await new NotificationProcessor(db, clock).ProcessAsync(OrderNotificationEvent.Parse(bytes, message.EventId.ToString("D"), message.EventType, "application/json"), default);
    }
    private async Task AssertEffectsAsync(OutboxMessage source, int count = 1)
    {
        await using var db = Database.CreateContext();
        Assert.Equal(count, await db.Notifications.CountAsync());
        Assert.Equal(count, await db.ProcessedMessages.CountAsync(m => m.ConsumerName == OrderNotificationEvent.ConsumerName && m.Status == InboxStatus.Processed));
        Assert.Equal(count, await db.OutboxMessages.CountAsync(m => m.EventType == "notifications.notification-created"));
        if (count == 1)
        {
            var n = await db.Notifications.SingleAsync(); var emitted = await db.OutboxMessages.SingleAsync(m => m.EventType == "notifications.notification-created");
            Assert.Equal(source.EventId, n.SourceEventId); Assert.Equal(source.AggregateId, n.OrderId);
            Assert.Equal(source.CorrelationId, emitted.CorrelationId); Assert.Equal(source.EventId, emitted.CausationId);
            Assert.Equal(n.Id, emitted.AggregateId); Assert.Equal(source.TraceParent, emitted.TraceParent);
        }
    }
    private static async Task<Task<byte[]>> ObserveAsync(IChannel channel, string queue)
    {
        var arrived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => { arrived.TrySetResult(delivery.Body.ToArray()); return Task.CompletedTask; };
        await channel.BasicConsumeAsync(queue, true, consumer);
        return arrived.Task;
    }

    [Fact]
    public async Task Simultaneous_real_deliveries_arbitrate_unique_inbox_and_emit_one_notification_and_outbox()
    {
        var source = await CreateAsync(); var settings = broker.Settings(prefix); var gate = new NotificationInsertGate();
        using var services = Services(gate); var log = new DeliveryLog<NotificationDeliveryHandler>(2); var handler = Handler(services, log);
        var options = new NotificationConsumerOptions { Prefetch = 1 };
        await using var first = await NotificationConsumerSubscription.StartAsync(settings, options, settings.NotificationQueue, handler, default);
        await using var second = await NotificationConsumerSubscription.StartAsync(settings, options, settings.NotificationQueue, handler, default);
        await using var publisher = new RabbitMqEventPublisher(settings);
        await publisher.PublishAsync(source, default); await publisher.PublishAsync(source, default);
        await log.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, gate.Participants); await AssertEffectsAsync(source);
        // The independent foundation queue still receives the event; notification consumption doesn't steal it.
        await using var connection = await settings.Factory().CreateConnectionAsync(); await using var channel = await connection.CreateChannelAsync();
        Assert.NotNull(await channel.BasicGetAsync(settings.Queue, true));
    }

    [Fact]
    public async Task Commit_before_ACK_fault_then_channel_restart_redelivers_without_duplicate_effects()
    {
        var source = await CreateAsync(); var settings = broker.Settings(prefix);
        await using var publisher = new RabbitMqEventPublisher(settings); await publisher.PublishAsync(source, default);
        using var services = Services(); var handler = Handler(services, new());
        await using var connection = await settings.Factory().CreateConnectionAsync();
        await using (var first = await connection.CreateChannelAsync())
        {
            var delivery = await first.BasicGetAsync(settings.NotificationQueue, false); Assert.NotNull(delivery);
            var probe = AckProbeChannel.Wrap(first, async () => { await AssertEffectsAsync(source); throw new InvalidOperationException("Injected crash before ACK"); });
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(delivery.Body, delivery.BasicProperties, probe, delivery.DeliveryTag, default));
        }
        await using var restarted = await connection.CreateChannelAsync();
        var redelivered = await restarted.BasicGetAsync(settings.NotificationQueue, false); Assert.NotNull(redelivered); Assert.True(redelivered.Redelivered);
        await handler.HandleAsync(redelivered.Body, redelivered.BasicProperties, restarted, redelivered.DeliveryTag, default);
        await AssertEffectsAsync(source); Assert.Null(await restarted.BasicGetAsync(settings.NotificationQueue, true));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("schema")]
    [InlineData("identity")]
    [InlineData("recipient")]
    [InlineData("oversized")]
    public async Task Poison_delivery_takes_finite_retry_path_to_notification_DLQ_without_database_effects(string fault)
    {
        var source = await CreateAsync(); var settings = broker.Settings(prefix);
        using var services = Services(); var log = new DeliveryLog<NotificationDeliveryHandler>(); var handler = Handler(services, log);
        await using var main = await NotificationConsumerSubscription.StartAsync(settings, new(), settings.NotificationQueue, handler, default);
        await using var retry = await NotificationConsumerSubscription.StartAsync(settings, new(), settings.NotificationRetryQueue, handler, default);
        await using var connection = await settings.Factory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        var dead = await ObserveAsync(channel, settings.NotificationDeadQueue);
        var body = EventEnvelope.Serialize(source);
        if (fault == "malformed") body = "{"u8.ToArray();
        if (fault == "schema") body = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(body).Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal));
        if (fault == "oversized") body = new byte[OrderNotificationEvent.MaximumBytes + 1];
        if (fault == "recipient")
        {
            using var payload = JsonDocument.Parse(source.Payload);
            body = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(body).Replace(payload.RootElement.GetProperty("userId").GetString()!, Guid.NewGuid().ToString("D"), StringComparison.Ordinal));
        }
        var properties = new BasicProperties { MessageId = fault == "identity" ? Guid.NewGuid().ToString("D") : source.EventId.ToString("D"),
            Type = source.EventType, Persistent = true, ContentType = "application/json" };
        await channel.BasicPublishAsync(settings.Exchange, source.EventType, true, properties, body);
        Assert.Equal(body, await dead.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(2, log.Events.Count(id => id == 1901)); await AssertEffectsAsync(source, 0);
    }

    [Fact]
    public async Task Transient_failure_rolls_back_all_three_effects_and_exhausts_finite_retry_into_DLQ()
    {
        var source = await CreateAsync(); var settings = broker.Settings(prefix);
        await using var db = Database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION messaging.fail_notification_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."EventType"='notifications.notification-created' THEN RAISE EXCEPTION 'Injected transient fault' USING ERRCODE='40001'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER "TR_NotificationFaultTest" BEFORE INSERT ON messaging."OutboxMessages" FOR EACH ROW EXECUTE FUNCTION messaging.fail_notification_test();
            """);
        using var services = Services(); var log = new DeliveryLog<NotificationDeliveryHandler>(); var handler = Handler(services, log);
        await using var main = await NotificationConsumerSubscription.StartAsync(settings, new(), settings.NotificationQueue, handler, default);
        await using var retry = await NotificationConsumerSubscription.StartAsync(settings, new(), settings.NotificationRetryQueue, handler, default);
        await using var connection = await settings.Factory().CreateConnectionAsync(); await using var channel = await connection.CreateChannelAsync();
        var dead = await ObserveAsync(channel, settings.NotificationDeadQueue);
        await using var publisher = new RabbitMqEventPublisher(settings); await publisher.PublishAsync(source, default);
        await dead.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, log.Events.Count(id => id == 1902)); await AssertEffectsAsync(source, 0);
    }

    [Fact]
    public async Task Independent_consumer_identity_can_process_same_event_and_changed_identity_is_rejected()
    {
        var source = await CreateAsync();
        await using (var db = Database.CreateContext())
        {
            var now = clock.GetUtcNow();
            db.ProcessedMessages.Add(new ProcessedMessage { ConsumerName = "reporting.test.v1", MessageId = source.EventId,
                MessageType = source.EventType, SchemaVersion = 1, Payload = "{}", PayloadHash = new byte[32], ReceivedAt = now,
                NextAttemptAt = now, Status = InboxStatus.Processed, ProcessedAt = now });
            await db.SaveChangesAsync();
        }
        await ProcessAsync(source); await ProcessAsync(source); await AssertEffectsAsync(source);
        var originalCorrelation = source.CorrelationId; source.CorrelationId = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<NotificationMessageException>(() => ProcessAsync(source));
        Assert.Equal("notification_event_identity_conflict", error.Code);
        source.CorrelationId = originalCorrelation; await AssertEffectsAsync(source);
        await using var read = Database.CreateContext(); Assert.Equal(2, await read.ProcessedMessages.CountAsync());
    }

    [Fact]
    public async Task PostgreSQL_enforces_notification_effect_identity_owner_and_monotonic_read_evidence()
    {
        var source = await CreateAsync(); await ProcessAsync(source);
        await using var db = Database.CreateContext(); var original = await db.Notifications.AsNoTracking().SingleAsync();
        await using (var duplicate = Database.CreateContext())
        {
            duplicate.Notifications.Add(new Notification { Id = Guid.NewGuid(), SourceEventId = original.SourceEventId,
                Type = original.Type, RecipientUserId = original.RecipientUserId, OrderId = original.OrderId, CreatedAt = original.CreatedAt });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState); Assert.Equal("UX_Notification_Source_Type_Recipient", postgres.ConstraintName);
        }
        var otherUser = await db.Users.Where(u => u.Id != original.RecipientUserId).Select(u => u.Id).FirstAsync();
        var wrongOwner = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications."Notifications" ("Id","RecipientUserId","SourceEventId","Type","OrderId","CreatedAt")
            VALUES ({Guid.NewGuid()},{otherUser},{original.SourceEventId},{original.Type},{original.OrderId},{original.CreatedAt})
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, wrongOwner.SqlState);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE notifications.\"Notifications\" SET \"ReadAt\"={clock.GetUtcNow()} WHERE \"Id\"={original.Id}");
        foreach (var sql in new[] { "UPDATE notifications.\"Notifications\" SET \"ReadAt\"=NULL",
            "UPDATE notifications.\"Notifications\" SET \"SourceEventId\"=gen_random_uuid()", "DELETE FROM notifications.\"Notifications\"",
            "DELETE FROM messaging.\"ProcessedMessages\" WHERE \"ConsumerName\"='notifications.orders.v1'" })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        await AssertEffectsAsync(source);
    }

    [Fact]
    public async Task Populated_previous_schema_upgrades_without_losing_original_event_or_order()
    {
        await using var legacy = await Server.CreateDatabaseAsync("20261112030213_AddOutboxPublishing");
        var now = clock.GetUtcNow();
        var user = new OrderFlow.Application.Modules.Identity.ApplicationUser { Id = Guid.NewGuid(), UserName = "legacy-notice@example.invalid", CreatedAt = now };
        var claim = IdempotencyRecord.Create(user.Id, OrderFlow.Application.Modules.Ordering.CheckoutRequest.Operation, "legacy-notice", "{}", now);
        claim.Complete(201, "application/json", "{}"u8.ToArray(), "/api/orders/legacy", now);
        var category = OrderFlow.Application.Modules.Catalog.Category.Create("Legacy notice", now);
        var product = OrderFlow.Application.Modules.Catalog.Product.Create("LEGACY-NOTICE", "Legacy", "",
            OrderFlow.Application.Common.Money.UsdMoney.FromDecimal(1m), category.Id, now);
        var order = OrderFlow.Application.Modules.Ordering.Order.Create(user.Id, claim.Id, [new(product.Id, product.Sku, product.Name, product.Price, 1)], now);
        var source = new OutboxMessage { EventId = Guid.NewGuid(), EventType = "ordering.order-created", SchemaVersion = 1,
            Producer = "Ordering", AggregateType = "Order", AggregateId = order.Id, AggregateVersion = 1, CorrelationId = order.Id,
            CausationId = claim.Id, OccurredAt = now, NextAttemptAt = now,
            Payload = JsonSerializer.Serialize(new { orderId = order.Id, userId = user.Id, version = 1, status = "PendingPayment" }) };
        await using var db = legacy.CreateContext();
        db.AddRange(user, claim, category, product, order, OrderFlow.Application.Modules.Ordering.OrderStatusHistory.Created(order), source);
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        await new NotificationProcessor(db, clock).ProcessAsync(OrderNotificationEvent.Parse(EventEnvelope.Serialize(source),
            source.EventId.ToString("D"), source.EventType, "application/json"), default);
        Assert.Equal(order.Id, (await db.Notifications.SingleAsync()).OrderId);
        Assert.Equal(source.EventId, (await db.Notifications.SingleAsync()).SourceEventId);
        Assert.Equal(2, await db.OutboxMessages.CountAsync()); Assert.Single(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Out_of_order_historical_events_notify_owner_without_regressing_order_or_disclosing_other_users()
    {
        var created = await CreateAsync();
        await using var host = new AuthApiFactory(Database, clock: clock);
        // Use application lifecycle under original owner's authenticated cookie via a separately created order below.
        var (owner, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var browser = owner;
        var products = await CheckoutTestData.StockAsync(Database, host, count: 1);
        var order = await OrderManagementTestData.CreateAsync(browser, CheckoutTestData.Body(products));
        using var cancelled = await OrderManagementTestData.CancelAsync(browser, order.Id); Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        await using var db = Database.CreateContext();
        var messages = await db.OutboxMessages.AsNoTracking().Where(m => m.AggregateId == order.Id).OrderByDescending(m => m.AggregateVersion).ToListAsync();
        foreach (var message in messages) await ProcessAsync(message);
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.OrderId == order.Id));
        Assert.Equal("Cancelled", (await db.Orders.SingleAsync(o => o.Id == order.Id)).Status.ToString());
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.SourceEventId == created.EventId));
    }
}
