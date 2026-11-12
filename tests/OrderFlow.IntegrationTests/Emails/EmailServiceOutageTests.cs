using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Payments;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Emails;

[Collection("Email"), Trait("Category", "PostgreSQL"), Trait("Category", "SMTP")]
public sealed class EmailServiceOutageTests(PostgresServer server, MailpitServer sink) : PostgresTest(server)
{
    [Fact]
    public async Task Orders_and_payments_continue_while_mail_sink_stopped_then_pending_email_recovers()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2035,1,1,0,0,0,TimeSpan.Zero));
        await using var provider = await Server.CreateDatabaseAsync(); await using var simulator = await SimulatorTestHost.StartAsync(provider, clock);
        await using var host = new AuthApiFactory(Database, clock: clock);
        var (browser, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var owner = browser;
        var products = await CheckoutTestData.StockAsync(Database, host, count: 1);
        using var gateway = simulator.Gateway();
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IPaymentGateway>(gateway); services.AddSingleton<IEmailGateway>(sink.Gateway());
        services.AddDbContext<OrderFlowDbContext>(options => PersistenceRegistration.Configure(options, Database.ConnectionString));
        services.AddSingleton<PaymentProcessingIteration>(); services.AddSingleton<PaymentReconciliationIteration>(); services.AddSingleton<EmailSendingIteration>();
        using var scopes = services.BuildServiceProvider(validateScopes: true);
        await sink.StopAsync();
        EmailDelivery email;
        try
        {
            // The mail service is actually stopped before both checkout and payment confirmation.
            var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
            using (var initiation = await PaymentInitiationTests.InitiateAsync(owner, order.Id)) await PaymentInitiationTests.AcceptedAsync(initiation);
            await using var db = Database.CreateContext(); var attempt = await db.PaymentAttempts.AsNoTracking().SingleAsync();
            await simulator.ScenarioAsync(attempt.ProviderRequestKey, "Succeeded");
            Assert.True(await scopes.GetRequiredService<PaymentProcessingIteration>().ProcessNextAsync(default));
            clock.Advance(TimeSpan.FromSeconds(5)); Assert.True(await scopes.GetRequiredService<PaymentReconciliationIteration>().ProcessNextAsync(default));
            Assert.Equal(OrderStatus.Confirmed, (await db.Orders.SingleAsync()).Status);
            var source = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventType == "ordering.order-confirmed");
            await new NotificationProcessor(db, clock).ProcessAsync(OrderNotificationEvent.Parse(EventEnvelope.Serialize(source), source.EventId.ToString("D"), source.EventType, "application/json"), default);
            Assert.True(await scopes.GetRequiredService<EmailSendingIteration>().ProcessNextAsync(default));
            email = await db.EmailDeliveries.AsNoTracking().SingleAsync(); Assert.Equal(EmailDeliveryStatus.Pending, email.Status);
            Assert.Equal(9, (await db.InventoryBalances.SingleAsync()).OnHand); Assert.Equal(0, (await db.InventoryBalances.SingleAsync()).Reserved);
            Assert.Single(await db.PaymentCaptures.ToListAsync()); Assert.Empty(await db.PaymentRefunds.ToListAsync());
        }
        finally { await sink.StartAsync(); }
        clock.Advance(email.NextAttemptAt - clock.GetUtcNow());
        // Docker may remap random fixture host ports on restart. Compose uses a fixed host port.
        services.AddSingleton<IEmailGateway>(sink.Gateway());
        using var restarted = services.BuildServiceProvider(validateScopes: true);
        Assert.True(await restarted.GetRequiredService<EmailSendingIteration>().ProcessNextAsync(default));
        await using var read = Database.CreateContext(); Assert.Equal(EmailDeliveryStatus.Sent, (await read.EmailDeliveries.SingleAsync()).Status);
        Assert.Equal(1, await sink.CountAsync(email.RecipientEmail));
    }
}
