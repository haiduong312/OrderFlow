using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Emails;

[Collection("PostgreSQL"), Trait("Category", "PostgreSQL")]
public sealed class EmailAdminTests(PostgresServer server) : PostgresTest(server)
{
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2035,1,1,0,0,0,TimeSpan.Zero));
    private async Task<EmailDelivery> ArrangeAsync(AuthApiFactory host, HttpClient owner, bool uncertain = true)
    {
        var products = await CheckoutTestData.StockAsync(Database, host, count: 1);
        var created = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products));
        await OrderManagementTestData.SeedStatusAsync(Database, created.Id, "Confirmed");
        await using var db = Database.CreateContext(); var order = await db.Orders.SingleAsync();
        var email = await db.Users.Where(u => u.Id == order.UserId).Select(u => u.Email).SingleAsync();
        var delivery = EmailDelivery.Confirmation(order, Guid.NewGuid(), email, clock.GetUtcNow());
        delivery.Status = EmailDeliveryStatus.NeedsReview; delivery.LastErrorCode = "controlled_failure";
        delivery.LastFailureAt = clock.GetUtcNow(); delivery.OutcomeUncertain = uncertain;
        db.EmailDeliveries.Add(delivery); await db.SaveChangesAsync(); return delivery;
    }
    private static Task<HttpResponseMessage> Retry(HttpClient client, Guid id, object body, string? token) =>
        BrowserRequests.SendAsync(client, HttpMethod.Post, $"/api/admin/email-deliveries/{id}/retry", body, token);

    [Fact]
    public async Task Failure_metadata_and_retry_require_admin_and_CSRF_and_never_expose_snapshots()
    {
        var gate = new CommandRaceGate(sql => sql.Contains("SELECT * FROM notifications.\"EmailDeliveries\"", StringComparison.Ordinal) && sql.Contains("FOR UPDATE", StringComparison.Ordinal));
        await using var host = new AuthApiFactory(Database, clock: clock, catalogInterceptor: gate); using var anonymous = host.Browser();
        var (customerClient, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var customer = customerClient;
        var (adminClient, _) = await AuthenticatedBrowser.CreateAsync(host, "Admin"); using var admin = adminClient;
        var delivery = await ArrangeAsync(host, customer);
        using (var response = await anonymous.GetAsync("/api/admin/email-deliveries")) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await customer.GetAsync("/api/admin/email-deliveries")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var metadata = await admin.GetAsync("/api/admin/email-deliveries"); Assert.True(metadata.Headers.CacheControl?.NoStore);
        var body = await metadata.Content.ReadAsStringAsync(); Assert.DoesNotContain(delivery.RecipientEmail, body, StringComparison.Ordinal);
        Assert.DoesNotContain(delivery.Subject, body, StringComparison.Ordinal); Assert.DoesNotContain(delivery.TextBody, body, StringComparison.Ordinal);
        var page = (await metadata.Content.ReadFromJsonAsync<EmailDeliveryPage>())!; Assert.Equal(delivery.Id, Assert.Single(page.Items).Id);
        var retry = new EmailRetryRequest(Guid.NewGuid(), "Sink restored", true);
        using (var response = await Retry(customer, delivery.Id, retry, await BrowserRequests.TokenAsync(customer))) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Retry(admin, delivery.Id, retry, null)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await Retry(admin, delivery.Id, retry with { AcknowledgeDuplicateRisk = false }, await BrowserRequests.TokenAsync(admin))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var csrf = await BrowserRequests.TokenAsync(admin);
        gate.Enabled = true;
        var responses = await Task.WhenAll(Retry(admin, delivery.Id, retry, csrf), Retry(admin, delivery.Id, retry, csrf));
        Assert.Equal(2, gate.Participants); gate.Enabled = false;
        foreach (var response in responses) { using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
        await using var db = Database.CreateContext(); var saved = await db.EmailDeliveries.SingleAsync(); Assert.Equal(delivery.Id, saved.Id);
        Assert.Equal(delivery.ProviderKey, saved.ProviderKey); Assert.Equal(1, saved.ReviewRetryCount); Assert.Equal(0, saved.AttemptsInCycle);
        Assert.True(saved.OutcomeUncertain); Assert.Single(await db.EmailDeliveryRetries.ToListAsync());
        using (var changed = await Retry(admin, delivery.Id, retry with { Reason = "Different operation facts" }, csrf)) Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using (var pending = await Retry(admin, delivery.Id, retry with { OperationId = Guid.NewGuid() }, csrf)) Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        using (var missing = await Retry(admin, Guid.NewGuid(), retry with { OperationId = Guid.NewGuid() }, csrf)) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var senderServices = new ServiceCollection(); senderServices.AddLogging(); senderServices.AddSingleton<TimeProvider>(clock);
        senderServices.AddSingleton<IEmailGateway>(new EmailTestGateway()); senderServices.AddSingleton<EmailSendingIteration>();
        senderServices.AddDbContext<OrderFlowDbContext>(options => PersistenceRegistration.Configure(options, Database.ConnectionString));
        using var sender = senderServices.BuildServiceProvider(validateScopes: true);
        Assert.True(await sender.GetRequiredService<EmailSendingIteration>().ProcessNextAsync(default));
        using (var replayAfterSent = await Retry(admin, delivery.Id, retry, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, replayAfterSent.StatusCode);
            Assert.Equal("Sent", (await replayAfterSent.Content.ReadFromJsonAsync<EmailDeliveryMetadata>())!.Status);
        }
        using (var sent = await Retry(admin, delivery.Id, retry with { OperationId = Guid.NewGuid() }, csrf)) Assert.Equal(HttpStatusCode.Conflict, sent.StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<EmailDeliveryPage>("/api/admin/email-deliveries"))!.Items);
    }
}
