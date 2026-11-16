using System.Net;
using System.Net.Http.Json;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Payments;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Frontend;

[Collection("PostgreSQL"), Trait("Category", "PostgreSQL")]
public sealed class CustomerPaymentStatusTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Payment_status_is_owner_only_paged_no_store_and_omits_provider_secrets()
    {
        await using var host = new AuthApiFactory(Database);
        var (owner, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var customer = owner;
        var products = await CheckoutTestData.StockAsync(Database, host, count: 1);
        var order = await OrderManagementTestData.CreateAsync(owner, CheckoutTestData.Body(products, 2));
        using (var initiated = await PaymentInitiationTests.InitiateAsync(owner, order.Id)) await PaymentInitiationTests.AcceptedAsync(initiated);
        using var response = await owner.GetAsync($"/api/orders/{order.Id:D}/payment-status?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"attemptCount\":1", body); Assert.Contains("\"status\":\"Queued\"", body);
        foreach (var secret in new[] { "providerRequestKey", "providerTransactionId", "providerCaptureId", "providerRefundKey", "lastObservation", "leaseToken", "rawBody" }) Assert.DoesNotContain(secret, body);
        using var second = await owner.GetAsync($"/api/orders/{order.Id:D}/payment-status?page=2&pageSize=1");
        Assert.Contains("\"attempts\":[]", await second.Content.ReadAsStringAsync());
        using var invalid = await owner.GetAsync($"/api/orders/{order.Id:D}/payment-status?page=0&pageSize=101"); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var (other, _) = await AuthenticatedBrowser.CreateAsync(host, "Customer"); using var otherOwner = other;
        using var denied = await other.GetAsync($"/api/orders/{order.Id:D}/payment-status"); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var anonymous = host.Browser(); using var unauthenticated = await anonymous.GetAsync($"/api/orders/{order.Id:D}/payment-status"); Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        var (admin, _) = await AuthenticatedBrowser.CreateAsync(host, "Admin"); using var administrator = admin;
        using var wrongRole = await admin.GetAsync($"/api/orders/{order.Id:D}/payment-status"); Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
    }
}
