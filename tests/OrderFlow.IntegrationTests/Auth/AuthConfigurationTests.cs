using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Auth;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class AuthConfigurationTests(PostgresDatabase database)
{
    [Fact]
    public async Task Secure_default_rejects_http_and_explicit_development_opt_in_uses_separate_cookies()
    {
        await using (var secure = new AuthApiFactory(database))
        {
            using var browser = secure.Browser(https: false);
            using var denied = await browser.GetAsync("/api/auth/antiforgery");
            await AuthenticationTests.Problem(denied, 400, "https_required");
        }

        await using var development = new AuthApiFactory(database, "Development", allowHttp: true);
        using var client = development.Browser(https: false);
        using var response = await client.GetAsync("/api/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("OrderFlow.Dev.Antiforgery=", cookie);
        Assert.DoesNotContain("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        await using (var scope = development.Services.CreateAsyncScope())
        {
            await DevelopmentSeeder.SeedRolesAsync(scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>(), default);
        }

        var account = new { email = AuthenticationTests.Email(), password = AuthenticationTests.Password() };
        var token = await AuthenticationTests.Token(client);
        using var registered = await AuthenticationTests.Post(client, "register", account, token);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var login = await AuthenticationTests.Post(client, "login", account, token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("OrderFlow.Dev.Auth=", StringComparison.Ordinal));
        Assert.DoesNotContain("; secure", authCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Insecure_http_opt_in_is_rejected_outside_development()
    {
        using var factory = new AuthApiFactory(database, allowHttp: true);
        Assert.Throws<InvalidOperationException>(() => factory.Browser());
    }

    [Fact]
    public async Task Authentication_write_limit_rejects_without_queuing_and_each_host_has_its_own_quota()
    {
        await using var firstHost = new AuthApiFactory(database);
        using var first = firstHost.Browser();
        // Invalid CSRF still consumes the quota, before password hashing or database access.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await AuthenticationTests.Post(first, "login", new { }, null);
            await AuthenticationTests.Problem(response, 400, "antiforgery_failed");
        }

        using var rejected = await AuthenticationTests.Post(first, "register", new { }, null);
        await AuthenticationTests.Problem(rejected, 429, "rate_limited");
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        await using var secondHost = new AuthApiFactory(database);
        using var second = secondHost.Browser();
        using var independent = await AuthenticationTests.Post(second, "login", new { }, null);
        await AuthenticationTests.Problem(independent, 400, "antiforgery_failed");
    }
}
