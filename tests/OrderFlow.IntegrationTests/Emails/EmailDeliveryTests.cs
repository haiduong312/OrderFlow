using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Notifications;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Emails;

[Collection("Email"), Trait("Category", "PostgreSQL"), Trait("Category", "SMTP")]
public sealed class EmailDeliveryTests(PostgresServer server, MailpitServer sink) : PostgresTest(server)
{
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2035,1,1,0,0,0,TimeSpan.Zero));
    private ServiceProvider Services(IEmailGateway gateway, DbCommandInterceptor? interceptor = null)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(clock); services.AddSingleton(gateway);
        services.AddScoped<NotificationProcessor>(); services.AddSingleton<EmailSendingIteration>();
        services.AddDbContext<OrderFlowDbContext>(options => { PersistenceRegistration.Configure(options, Database.ConnectionString); if (interceptor is not null) options.AddInterceptors(interceptor); });
        return services.BuildServiceProvider(validateScopes: true);
    }
    private async Task<bool> SendAsync(IEmailGateway gateway, DbCommandInterceptor? interceptor = null)
    {
        using var services = Services(gateway, interceptor); return await services.GetRequiredService<EmailSendingIteration>().ProcessNextAsync(default);
    }
    private async Task<OutboxMessage> SourceAsync(bool process = true)
    {
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (browser, userId) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var owner = browser;
        var products = await CheckoutTestData.StockAsync(Database, host, count: 1);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        // This fixture seeds the existing fulfillment status/history convention; a separate demo uses real payment settlement.
        await OrderManagementTestData.SeedStatusAsync(Database, order.Id, "Confirmed");
        var source = new OutboxMessage { EventId = Guid.NewGuid(), EventType = "ordering.order-confirmed", SchemaVersion = 1,
            Producer = "Ordering", AggregateType = "Order", AggregateId = order.Id, AggregateVersion = 2,
            CorrelationId = order.Id, CausationId = Guid.NewGuid(), OccurredAt = clock.GetUtcNow(), NextAttemptAt = clock.GetUtcNow(),
            Payload = JsonSerializer.Serialize(new { orderId = order.Id, userId, version = 2, status = "Confirmed" }) };
        await using var db = Database.CreateContext(); db.OutboxMessages.Add(source); await db.SaveChangesAsync();
        if (process) await ProcessAsync(source);
        return source;
    }
    private async Task ProcessAsync(OutboxMessage source, DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<OrderFlowDbContext>(Database.Options()); if (interceptor is not null) options.AddInterceptors(interceptor);
        await using var db = new OrderFlowDbContext(options.Options);
        await new NotificationProcessor(db, clock).ProcessAsync(OrderNotificationEvent.Parse(EventEnvelope.Serialize(source), source.EventId.ToString("D"), source.EventType, "application/json"), default);
    }
    private async Task<EmailDelivery> ReadAsync() { await using var db = Database.CreateContext(); return await db.EmailDeliveries.AsNoTracking().SingleAsync(); }
    private async Task DueAsync() { var row = await ReadAsync(); clock.Advance(row.NextAttemptAt - clock.GetUtcNow()); }
    private static EmailTestGateway Failure(bool retryable) => new() { OnSend = (_, _) => throw new EmailGatewayException("controlled_failure", retryable, false) };

    [Fact]
    public async Task Concurrent_confirmations_create_one_intention_and_handled_inbox_atomically_with_snapshots()
    {
        var source = await SourceAsync(false); var gate = new NotificationInsertGate();
        await Task.WhenAll(ProcessAsync(source, gate), ProcessAsync(source, gate)); Assert.Equal(2, gate.Participants);
        var delivery = await ReadAsync(); Assert.Equal(source.EventId, delivery.SourceEventId); Assert.Equal(source.AggregateId, delivery.OrderId);
        await using var db = Database.CreateContext(); Assert.Single(await db.Notifications.ToListAsync()); Assert.Single(await db.ProcessedMessages.ToListAsync());
        var originalRecipient = delivery.RecipientEmail;
        await db.Users.Where(u => u.Id == delivery.RecipientUserId).ExecuteUpdateAsync(u => u.SetProperty(p => p.Email, "changed@example.invalid"));
        var gateway = new EmailTestGateway { OnSend = (request, _) => { Assert.Equal(originalRecipient, request.Recipient); Assert.Equal(delivery.ProviderKey, request.ProviderKey);
            Assert.Contains("12.50 USD", request.TextBody, StringComparison.Ordinal); return Task.FromResult(new EmailGatewayResult(request.ProviderKey, request.RequestHash, EmailProviderState.Sent)); } };
        Assert.True(await SendAsync(new ProbeEmailGateway(gateway, Database))); Assert.Equal(EmailDeliveryStatus.Sent, (await ReadAsync()).Status);
        await ProcessAsync(source); Assert.Equal(delivery.Id, (await ReadAsync()).Id);
    }

    [Fact]
    public async Task Intention_rolls_back_with_inbox_notification_and_outbox_on_consumer_failure()
    {
        var source = await SourceAsync(false); await using var db = Database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION notifications.email_intention_fault_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."EventType"='notifications.notification-created' THEN RAISE EXCEPTION 'controlled fault'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER "TR_EmailIntentionFaultTest" BEFORE INSERT ON messaging."OutboxMessages" FOR EACH ROW EXECUTE FUNCTION notifications.email_intention_fault_test();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => ProcessAsync(source));
        Assert.Empty(await db.EmailDeliveries.ToListAsync()); Assert.Empty(await db.ProcessedMessages.ToListAsync()); Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData(true, EmailDeliveryStatus.Pending)]
    [InlineData(false, EmailDeliveryStatus.NeedsReview)]
    public async Task Classified_failure_is_durable_and_has_bounded_schedule(bool retryable, EmailDeliveryStatus expected)
    {
        await SourceAsync(); var before = await ReadAsync(); Assert.True(await SendAsync(Failure(retryable)));
        var after = await ReadAsync(); Assert.Equal(expected, after.Status); Assert.Equal(1, after.AttemptCount); Assert.Equal(1, after.AttemptsInCycle);
        Assert.Equal(before.ProviderKey, after.ProviderKey); Assert.Equal(clock.GetUtcNow().AddSeconds(5), after.NextAttemptAt);
        Assert.Equal("controlled_failure", after.LastErrorCode); Assert.False(after.OutcomeUncertain); Assert.Null(after.LeaseToken);
        Assert.False(await SendAsync(Failure(true)));
    }

    [Fact]
    public async Task Provider_timeout_after_success_is_looked_up_after_restart_using_original_key()
    {
        await SourceAsync(); var gateway = new EmailTestGateway { SupportsIdempotentSend = true, SupportsLookup = true };
        gateway.OnSend = (request, _) => { gateway.Accept(request); throw new EmailGatewayException("controlled_timeout", true, true); };
        Assert.True(await SendAsync(new ProbeEmailGateway(gateway, Database))); Assert.True((await ReadAsync()).OutcomeUncertain);
        await DueAsync(); Assert.True(await SendAsync(new ProbeEmailGateway(gateway, Database)));
        Assert.Equal(EmailDeliveryStatus.Sent, (await ReadAsync()).Status); Assert.Equal(1, gateway.SendCalls); Assert.Equal(1, gateway.EffectiveSends); Assert.Equal(2, gateway.LookupCalls);
    }

    [Fact]
    public async Task Idempotent_provider_without_lookup_reuses_same_key_and_has_one_effective_send()
    {
        await SourceAsync(); var gateway = new EmailTestGateway { SupportsIdempotentSend = true };
        gateway.OnSend = (request, _) => { gateway.Accept(request); gateway.OnSend = null; throw new EmailGatewayException("controlled_timeout", true, true); };
        await SendAsync(gateway); await DueAsync(); await SendAsync(gateway);
        Assert.Equal(2, gateway.SendCalls); Assert.Equal(1, gateway.EffectiveSends); Assert.Equal(0, gateway.LookupCalls);
    }

    [Fact]
    public async Task Lost_local_ack_is_reconciled_after_lease_expiry_and_stale_owner_cannot_overwrite()
    {
        await SourceAsync(); var gateway = new EmailTestGateway { SupportsIdempotentSend = true, SupportsLookup = true };
        await using var db = Database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION notifications.email_ack_fault_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."Status"='Sent' THEN RAISE EXCEPTION 'controlled local acknowledgement fault'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER "TR_EmailAckFaultTest" BEFORE UPDATE ON notifications."EmailDeliveries" FOR EACH ROW EXECUTE FUNCTION notifications.email_ack_fault_test();
            """);
        await Assert.ThrowsAsync<PostgresException>(() => SendAsync(gateway));
        Assert.Equal(EmailDeliveryStatus.InFlight, (await ReadAsync()).Status);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER \"TR_EmailAckFaultTest\" ON notifications.\"EmailDeliveries\"; DROP FUNCTION notifications.email_ack_fault_test();");
        clock.Advance(TimeSpan.FromSeconds(60)); await SendAsync(gateway);
        Assert.Equal(1, gateway.EffectiveSends); Assert.Equal(1, gateway.SendCalls); Assert.Equal(EmailDeliveryStatus.Sent, (await ReadAsync()).Status);
    }

    [Fact]
    public async Task Two_workers_claim_with_skip_locked_and_only_one_sends()
    {
        await SourceAsync(); var gateway = new EmailTestGateway { SupportsIdempotentSend = true };
        var gate = new CommandRaceGate(sql => sql.Contains("SELECT * FROM notifications.\"EmailDeliveries\"", StringComparison.Ordinal) && sql.Contains("SKIP LOCKED", StringComparison.Ordinal)) { Enabled = true };
        var results = await Task.WhenAll(SendAsync(gateway, gate), SendAsync(gateway, gate));
        Assert.Equal(2, gate.Participants); Assert.Single(results, r => r); Assert.Equal(1, gateway.SendCalls);
    }

    [Fact]
    public async Task Expired_owner_after_provider_effect_cannot_ack_newer_claim()
    {
        await SourceAsync(); var gateway = new EmailTestGateway { SupportsIdempotentSend = true, SupportsLookup = true };
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.OnSend = async (request, token) => { var result = gateway.Accept(request); reached.TrySetResult(); await release.Task.WaitAsync(token); return result; };
        var old = SendAsync(gateway);
        try { await reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); clock.Advance(TimeSpan.FromSeconds(60)); await SendAsync(gateway); }
        finally { release.TrySetResult(); }
        await old; Assert.Equal(EmailDeliveryStatus.Sent, (await ReadAsync()).Status); Assert.Equal(2, (await ReadAsync()).AttemptCount); Assert.Equal(1, gateway.EffectiveSends);
    }

    [Fact]
    public async Task Shutdown_after_provider_acceptance_leaves_uncertain_claim_for_recovery()
    {
        await SourceAsync(); var gateway = new EmailTestGateway { SupportsIdempotentSend = true, SupportsLookup = true };
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.OnSend = async (request, token) => { var result = gateway.Accept(request); reached.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return result; };
        using var services = Services(gateway); using var shutdown = new CancellationTokenSource();
        var sending = services.GetRequiredService<EmailSendingIteration>().ProcessNextAsync(shutdown.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); shutdown.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        var row = await ReadAsync(); Assert.True(row.OutcomeUncertain); Assert.Equal(EmailDeliveryStatus.InFlight, row.Status);
        clock.Advance(TimeSpan.FromSeconds(60)); await SendAsync(gateway); Assert.Equal(1, gateway.EffectiveSends); Assert.Equal(EmailDeliveryStatus.Sent, (await ReadAsync()).Status);
    }

    [Fact]
    public async Task Exhausted_final_crash_becomes_needs_review_without_a_ninth_automatic_attempt()
    {
        await SourceAsync(); var gateway = Failure(true);
        for (var i = 0; i < 7; i++) { await SendAsync(gateway); await DueAsync(); }
        gateway.OnSend = (_, _) => throw new InvalidOperationException("controlled crash");
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(gateway)); clock.Advance(TimeSpan.FromSeconds(60));
        Assert.False(await SendAsync(gateway)); var row = await ReadAsync(); Assert.Equal(EmailDeliveryStatus.NeedsReview, row.Status);
        Assert.Equal(8, row.AttemptCount); Assert.Equal("email_attempts_exhausted", row.LastErrorCode); Assert.True(row.OutcomeUncertain);
    }

    [Theory]
    [InlineData(EmailProviderState.Pending, "email_attempts_exhausted")]
    [InlineData(EmailProviderState.Rejected, "email_provider_rejected")]
    public async Task Lookup_pending_or_rejected_does_not_blindly_send(EmailProviderState state, string error)
    {
        await SourceAsync(); var gateway = new EmailTestGateway { SupportsLookup = true,
            OnLookup = (request, _) => Task.FromResult(new EmailGatewayResult(request.ProviderKey, request.RequestHash, state)) };
        for (var i = 0; i < (state == EmailProviderState.Pending ? 8 : 1); i++) { await SendAsync(gateway); await DueAsync(); }
        var row = await ReadAsync(); Assert.Equal(EmailDeliveryStatus.NeedsReview, row.Status); Assert.Equal(error, row.LastErrorCode); Assert.Equal(0, gateway.SendCalls);
    }

    [Fact]
    public async Task Lookup_mismatch_and_absent_after_uncertain_nonidempotent_send_require_review()
    {
        await SourceAsync(); var gateway = new EmailTestGateway { SupportsLookup = true };
        gateway.OnSend = (request, _) => { gateway.Accept(request); throw new EmailGatewayException("controlled_timeout", true, true); };
        await SendAsync(gateway); await DueAsync();
        gateway.OnLookup = (request, _) => Task.FromResult(new EmailGatewayResult(request.ProviderKey, request.RequestHash, EmailProviderState.Absent));
        await SendAsync(gateway); Assert.Equal("email_absent_after_uncertain_send", (await ReadAsync()).LastErrorCode); Assert.Equal(1, gateway.SendCalls);
    }

    [Fact]
    public async Task Different_source_identity_for_same_confirmation_retains_original_delivery_and_provider_key()
    {
        var source = await SourceAsync(); var first = await ReadAsync(); source.EventId = Guid.NewGuid();
        await ProcessAsync(source); var second = await ReadAsync(); Assert.Equal(first.Id, second.Id); Assert.Equal(first.ProviderKey, second.ProviderKey);
        Assert.Equal(first.SourceEventId, second.SourceEventId);
    }

    [Fact]
    public async Task Mismatched_provider_result_and_development_real_address_are_reviewed_without_false_sent()
    {
        await SourceAsync(); var gateway = new EmailTestGateway { OnSend = (request, _) => Task.FromResult(new EmailGatewayResult("wrong-key", request.RequestHash, EmailProviderState.Sent)) };
        await SendAsync(gateway); Assert.Equal("email_response_mismatch", (await ReadAsync()).LastErrorCode); Assert.Null((await ReadAsync()).SentAt);
        var smtp = sink.Gateway(); var request = (await ReadAsync()).Request() with { Recipient = "customer@example.com" };
        var failure = await Assert.ThrowsAsync<EmailGatewayException>(() => smtp.SendAsync(request, default));
        Assert.False(failure.Retryable); Assert.False(failure.Uncertain); Assert.Equal("email_development_recipient_rejected", failure.Code);
    }

    [Fact]
    public async Task PostgreSQL_preserves_unique_effect_snapshots_sent_evidence_and_audited_retry_cycles()
    {
        await SourceAsync(); var row = await ReadAsync(); await using var db = Database.CreateContext();
        var duplicate = EmailDelivery.Confirmation(await db.Orders.SingleAsync(), Guid.NewGuid(), row.RecipientEmail, clock.GetUtcNow());
        db.EmailDeliveries.Add(duplicate);
        var unique = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(unique.InnerException).SqlState); db.ChangeTracker.Clear();
        foreach (var sql in new[] { "UPDATE notifications.\"EmailDeliveries\" SET \"ProviderKey\"='replacement'",
            "UPDATE notifications.\"EmailDeliveries\" SET \"RecipientEmail\"='changed@example.invalid'",
            "DELETE FROM notifications.\"EmailDeliveries\"" })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        await SendAsync(Failure(false));
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE notifications.\"EmailDeliveries\" SET \"Status\"='Pending',\"AttemptsInCycle\"=0,\"ReviewRetryCount\"=1"))).SqlState);
    }

    [Fact]
    public async Task Actual_SMTP_sink_can_receive_duplicate_after_local_result_is_lost()
    {
        await SourceAsync(); var row = await ReadAsync(); var smtp = sink.Gateway();
        await using var db = Database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION notifications.smtp_ack_fault_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."Status"='Sent' THEN RAISE EXCEPTION 'controlled local acknowledgement fault'; END IF; RETURN NEW; END; $body$;
            CREATE TRIGGER "TR_SmtpAckFaultTest" BEFORE UPDATE ON notifications."EmailDeliveries" FOR EACH ROW EXECUTE FUNCTION notifications.smtp_ack_fault_test();
            """);
        await Assert.ThrowsAsync<PostgresException>(() => SendAsync(new ProbeEmailGateway(smtp, Database)));
        Assert.Equal(1, await sink.CountAsync(row.RecipientEmail));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER \"TR_SmtpAckFaultTest\" ON notifications.\"EmailDeliveries\"; DROP FUNCTION notifications.smtp_ack_fault_test();");
        clock.Advance(TimeSpan.FromSeconds(60)); await SendAsync(new ProbeEmailGateway(smtp, Database));
        Assert.Equal(EmailDeliveryStatus.Sent, (await ReadAsync()).Status); Assert.Equal(row.ProviderKey, (await ReadAsync()).ProviderKey);
        Assert.Equal(2, await sink.CountAsync(row.RecipientEmail)); // Stable Message-ID does not provide SMTP idempotency.
    }
}

internal sealed class ProbeEmailGateway(IEmailGateway inner, PostgresDatabase database) : IEmailGateway
{
    public string Name => inner.Name;
    public bool SupportsIdempotentSend => inner.SupportsIdempotentSend;
    public bool SupportsLookup => inner.SupportsLookup;
    private async Task ProbeAsync(EmailRequest request, CancellationToken token)
    {
        await using var db = database.CreateContext(); await using var transaction = await db.Database.BeginTransactionAsync(token);
        Assert.Single(await db.EmailDeliveries.FromSqlInterpolated($"SELECT * FROM notifications.\"EmailDeliveries\" WHERE \"Id\"={request.DeliveryId} FOR UPDATE NOWAIT").ToListAsync(token));
        await transaction.RollbackAsync(token);
    }
    public async Task<EmailGatewayResult> LookupAsync(EmailRequest request, CancellationToken token) { await ProbeAsync(request, token); return await inner.LookupAsync(request, token); }
    public async Task<EmailGatewayResult> SendAsync(EmailRequest request, CancellationToken token) { await ProbeAsync(request, token); return await inner.SendAsync(request, token); }
}
