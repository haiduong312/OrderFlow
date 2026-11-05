using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Authorization;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResourceAuthorizationHttpTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Customer_reads_own_resource_and_other_customers_resource_is_indistinguishable_from_missing()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, ownerId) = await Browser(factory, "Customer");
        using var browser = client;
        var own = Store(factory).Add(ownerId);
        var other = Store(factory).Add(Guid.NewGuid());
        using var allowed = await browser.GetAsync(Customer(own.Id));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var hidden = await browser.GetAsync(Customer(other.Id));
        using var missing = await browser.GetAsync(Customer(Guid.NewGuid()));
        var hiddenBody = await AuthenticationTests.Problem(hidden, 404, "not_found");
        var missingBody = await AuthenticationTests.Problem(missing, 404, "not_found");
        Assert.Equal(WithoutTrace(missingBody), WithoutTrace(hiddenBody));
        Assert.True(hidden.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain(other.OwnerId.ToString(), hiddenBody.GetRawText());
        Assert.DoesNotContain(other.Id.ToString(), hiddenBody.GetRawText());
    }

    [Fact]
    public async Task Anonymous_and_wrong_role_denials_do_not_depend_on_resource_existence()
    {
        await using var factory = new AuthApiFactory(Database);
        var (customerClient, customerId) = await Browser(factory, "Customer");
        var (adminClient, _) = await Browser(factory, "Admin");
        using var customer = customerClient;
        using var admin = adminClient;
        using var anonymous = factory.Browser();
        var existing = Store(factory).Add(customerId);
        foreach (var id in new[] { existing.Id, Guid.NewGuid() })
        {
            using var anonymousCustomer = await anonymous.GetAsync(Customer(id));
            using var anonymousAdmin = await anonymous.GetAsync(Admin(id));
            await AuthenticationTests.Problem(anonymousCustomer, 401, "unauthenticated");
            await AuthenticationTests.Problem(anonymousAdmin, 401, "unauthenticated");
            Assert.Null(anonymousCustomer.Headers.Location);
            using var customerOnAdmin = await customer.GetAsync(Admin(id));
            await AuthenticationTests.Problem(customerOnAdmin, 403, "forbidden");
            using var adminOnCustomer = await admin.GetAsync(Customer(id));
            await AuthenticationTests.Problem(adminOnCustomer, 403, "forbidden");
            Assert.Null(adminOnCustomer.Headers.Location);
        }
    }

    [Fact]
    public async Task Admin_can_read_any_resource_on_admin_path_but_missing_stays_404()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await Browser(factory, "Admin");
        using var admin = client;
        var resource = Store(factory).Add(Guid.NewGuid());
        using var allowed = await admin.GetAsync(Admin(resource.Id));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var missing = await admin.GetAsync(Admin(Guid.NewGuid()));
        await AuthenticationTests.Problem(missing, 404, "not_found");
    }

    [Fact]
    public async Task Dual_role_admin_must_still_own_resources_on_customer_path()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, id) = await Browser(factory, "Admin", "Customer");
        using var browser = client;
        var own = Store(factory).Add(id);
        var other = Store(factory).Add(Guid.NewGuid());
        using var allowed = await browser.GetAsync(Customer(own.Id));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var hidden = await browser.GetAsync(Customer(other.Id));
        await AuthenticationTests.Problem(hidden, 404, "not_found");
        using var adminAllowed = await browser.GetAsync(Admin(other.Id));
        Assert.Equal(HttpStatusCode.OK, adminAllowed.StatusCode);
    }

    [Fact]
    public async Task Query_and_headers_cannot_override_the_cookie_identity_or_role()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await Browser(factory, "Customer");
        using var browser = client;
        var other = Store(factory).Add(Guid.NewGuid());
        using var request = new HttpRequestMessage(HttpMethod.Get, Customer(other.Id) + "?userId=" + other.OwnerId);
        request.Headers.Add("X-User-Id", other.OwnerId.ToString());
        request.Headers.Add("X-Role", "Admin");
        using var denied = await browser.SendAsync(request);
        await AuthenticationTests.Problem(denied, 404, "not_found");
        using var privileged = new HttpRequestMessage(HttpMethod.Get, Admin(other.Id));
        privileged.Headers.Add("X-Role", "Admin");
        using var forbidden = await browser.SendAsync(privileged);
        await AuthenticationTests.Problem(forbidden, 403, "forbidden");
    }

    [Fact]
    public async Task Admin_permission_never_bypasses_antiforgery_or_business_state_rules()
    {
        await using var factory = new AuthApiFactory(Database);
        var (client, _) = await Browser(factory, "Admin");
        using var admin = client;
        var resource = Store(factory).Add(Guid.NewGuid(), closed: true);
        var url = Admin(resource.Id) + "/check-close";
        using var noCsrf = await admin.PostAsync(url, null);
        await AuthenticationTests.Problem(noCsrf, 400, "antiforgery_failed");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-CSRF-TOKEN", await BrowserRequests.TokenAsync(admin));
        using var invalidTransition = await admin.SendAsync(request);
        await AuthenticationTests.Problem(invalidTransition, 409, "state_conflict");
        Assert.True(Store(factory).Find(resource.Id)!.IsClosed);
    }

    private static string Customer(Guid id) => "/_tests/resource-authorization/customer/" + id;
    private static string Admin(Guid id) => "/_tests/resource-authorization/admin/" + id;
    private static RepresentativeResourceStore Store(AuthApiFactory factory) => factory.Services.GetRequiredService<RepresentativeResourceStore>();
    private static string WithoutTrace(JsonElement problem) => JsonSerializer.Serialize(problem.EnumerateObject()
        .Where(property => property.Name != "traceId").ToDictionary(property => property.Name, property => property.Value));

    private static Task<(HttpClient Client, Guid Id)> Browser(AuthApiFactory factory, params string[] roles) =>
        AuthenticatedBrowser.CreateAsync(factory, roles);
}
