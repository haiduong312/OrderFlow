using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Api.Auth;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Auth;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class AuthenticationTests(PostgresServer server) : PostgresTest(server)
{
    [Fact]
    public async Task Register_login_me_and_logout_use_cookie_and_refresh_csrf_tokens()
    {
        await PrepareRoles();
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        var email = Email();
        var password = Password();
        var anonymousToken = await BrowserRequests.TokenAsync(client);
        using var registration = await BrowserRequests.PostAuthAsync(client, "register", new { email, password }, anonymousToken);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var created = await registration.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(new[] { "Customer" }, created!.Roles);
        using var anonymousMe = await client.GetAsync("/api/auth/me");
        await Problem(anonymousMe, 401, "unauthenticated");

        using var login = await BrowserRequests.PostAuthAsync(client, "login", new { email, password }, anonymousToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-OrderFlow.Auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(created.Id, me!.Id);
        Assert.Equal(email, me.Email);

        using var stale = await BrowserRequests.PostAuthAsync(client, "logout", new { }, anonymousToken);
        await Problem(stale, 400, "antiforgery_failed");
        var authenticatedToken = await BrowserRequests.TokenAsync(client);
        using var logout = await BrowserRequests.PostAuthAsync(client, "logout", new { }, authenticatedToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/api/auth/me");
        await Problem(afterLogout, 401, "unauthenticated");
        using var staleAuthenticated = await BrowserRequests.PostAuthAsync(client, "login", new { email, password }, authenticatedToken);
        await Problem(staleAuthenticated, 400, "antiforgery_failed");
        Assert.False(string.IsNullOrWhiteSpace(await BrowserRequests.TokenAsync(client)));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await users.FindByEmailAsync(email);
        Assert.NotEqual(password, stored!.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(stored, password));
        Assert.False(stored.EmailConfirmed);
        Assert.Equal(TimeSpan.Zero, stored.CreatedAt.Offset);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Message.Contains(password)
            || entry.Message.Contains(anonymousToken) || entry.Message.Contains(authenticatedToken));
    }

    [Fact]
    public async Task Duplicate_case_normalized_accounts_are_rejected_without_orphans()
    {
        await PrepareRoles();
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        var email = Email();
        var password = Password();
        using var first = await BrowserRequests.PostAuthAsync(client, "register", new { email, password }, await BrowserRequests.TokenAsync(client));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var duplicate = await BrowserRequests.PostAuthAsync(client, "register", new { email = email.ToUpperInvariant(), password }, await BrowserRequests.TokenAsync(client));
        await Problem(duplicate, 400, "validation_failed");
        using var login = await BrowserRequests.PostAuthAsync(client, "login", new { email = email.ToUpperInvariant(), password }, await BrowserRequests.TokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await using var context = Database.CreateContext();
        Assert.Equal(1, await context.Users.CountAsync(user => user.NormalizedEmail == email.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("roles", "[\"Admin\"]")]
    [InlineData("role", "\"Admin\"")]
    [InlineData("isAdmin", "true")]
    [InlineData("emailConfirmed", "true")]
    [InlineData("passwordHash", "\"sensitive-test-marker\"")]
    [InlineData("id", "\"00000000-0000-0000-0000-000000000001\"")]
    public async Task Registration_rejects_privileged_fields(string field, string value)
    {
        await PrepareRoles();
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        var email = Email();
        var payload = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["password"] = Password(),
            [field] = JsonSerializer.Deserialize<JsonElement>(value)
        };
        using var response = await BrowserRequests.PostAuthAsync(client, "register", payload, await BrowserRequests.TokenAsync(client));
        await Problem(response, 400, "validation_failed");
        await using var context = Database.CreateContext();
        Assert.False(await context.Users.AnyAsync(user => user.Email == email));
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    public async Task Missing_invalid_and_unpaired_csrf_are_rejected_for_anonymous_auth(string operation)
    {
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        var payload = new { email = Email(), password = Password() };
        using var missing = await BrowserRequests.PostAuthAsync(client, operation, payload, null);
        await Problem(missing, 400, "antiforgery_failed");
        var valid = await BrowserRequests.TokenAsync(client);
        using var invalid = await BrowserRequests.PostAuthAsync(client, operation, payload, "invalid-test-token");
        await Problem(invalid, 400, "antiforgery_failed");
        using var differentBrowser = factory.Browser();
        using var unpaired = await BrowserRequests.PostAuthAsync(differentBrowser, operation, payload, valid);
        await Problem(unpaired, 400, "antiforgery_failed");
    }

    [Fact]
    public async Task Missing_logout_csrf_is_rejected_and_does_not_end_session()
    {
        await PrepareRoles();
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        var payload = new { email = Email(), password = Password() };
        using var registered = await BrowserRequests.PostAuthAsync(client, "register", payload, await BrowserRequests.TokenAsync(client));
        using var login = await BrowserRequests.PostAuthAsync(client, "login", payload, await BrowserRequests.TokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var denied = await BrowserRequests.PostAuthAsync(client, "logout", new { }, null);
        await Problem(denied, 400, "antiforgery_failed");
        using var stillLoggedIn = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillLoggedIn.StatusCode);
    }

    [Fact]
    public async Task Invalid_credentials_and_lockout_return_same_generic_401()
    {
        await PrepareRoles();
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        var email = Email();
        var password = Password();
        using var register = await BrowserRequests.PostAuthAsync(client, "register", new { email, password }, await BrowserRequests.TokenAsync(client));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var token = await BrowserRequests.TokenAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await BrowserRequests.PostAuthAsync(client, "login", new { email, password = Password() }, token);
            await Problem(failure, 401, "invalid_credentials");
        }

        using var locked = await BrowserRequests.PostAuthAsync(client, "login", new { email, password }, token);
        var lockedBody = await Problem(locked, 401, "invalid_credentials");
        using var unknown = await BrowserRequests.PostAuthAsync(client, "login", new { email = Email(), password }, token);
        var unknownBody = await Problem(unknown, 401, "invalid_credentials");
        Assert.Equal(lockedBody.GetProperty("title").GetString(), unknownBody.GetProperty("title").GetString());

        await using (var context = Database.CreateContext())
        {
            var user = await context.Users.SingleAsync(user => user.Email == email);
            Assert.NotNull(user.LockoutEnd);
            Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
            user.LockoutEnd = DateTimeOffset.UnixEpoch; // Test expiry without a real 15-minute wait.
            await context.SaveChangesAsync();
        }

        using var unlocked = await BrowserRequests.PostAuthAsync(client, "login", new { email, password }, token);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
    }

    [Fact]
    public async Task Policies_return_401_or_403_without_login_redirects()
    {
        await PrepareRoles();
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        using var anonymous = await client.GetAsync("/_tests/auth/admin");
        await Problem(anonymous, 401, "unauthenticated");
        Assert.Null(anonymous.Headers.Location);
        using var logout = await BrowserRequests.PostAuthAsync(client, "logout", new { }, null);
        await Problem(logout, 401, "unauthenticated");
        var payload = new { email = Email(), password = Password() };
        using var registered = await BrowserRequests.PostAuthAsync(client, "register", payload, await BrowserRequests.TokenAsync(client));
        using var login = await BrowserRequests.PostAuthAsync(client, "login", payload, await BrowserRequests.TokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var forbidden = await client.GetAsync("/_tests/auth/admin");
        await Problem(forbidden, 403, "forbidden");
        Assert.Null(forbidden.Headers.Location);
        using var customer = await client.GetAsync("/_tests/auth/customer");
        Assert.Equal(HttpStatusCode.OK, customer.StatusCode);
    }

    [Fact]
    public async Task Development_account_seed_requires_opt_in_and_never_promotes_existing_customer()
    {
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var options = new DevelopmentAccountOptions { Email = Email(), Password = Password(), Role = "Admin" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentAccountSeeder.SeedAsync("Development", options, users, context, clock, default));
        options.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentAccountSeeder.SeedAsync("Production", options, users, context, clock, default));
        Assert.False(await context.Users.AnyAsync(user => user.Email == options.Email));
        Assert.True(await DevelopmentAccountSeeder.SeedAsync("Development", options, users, context, clock, default));
        var stored = (await users.FindByEmailAsync(options.Email!))!;
        var originalHash = stored.PasswordHash;
        var password = options.Password;
        options.Password = Password();
        Assert.False(await DevelopmentAccountSeeder.SeedAsync("Development", options, users, context, clock, default));
        Assert.Equal(originalHash, stored.PasswordHash);

        using var login = await BrowserRequests.PostAuthAsync(client, "login", new { email = options.Email, password }, await BrowserRequests.TokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var admin = await client.GetAsync("/_tests/auth/admin");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        using var customer = await client.GetAsync("/_tests/auth/customer");
        await Problem(customer, 403, "forbidden");
        options.Role = "Customer";
        await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentAccountSeeder.SeedAsync("Development", options, users, context, clock, default));
        Assert.Equal(new[] { "Admin" }, await users.GetRolesAsync(stored));

        options.Email = Email();
        options.Role = "Customer";
        Assert.True(await DevelopmentAccountSeeder.SeedAsync("Development", options, users, context, clock, default));
        options.Role = "Admin";
        await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentAccountSeeder.SeedAsync("Development", options, users, context, clock, default));
        Assert.Equal(new[] { "Customer" }, await users.GetRolesAsync((await users.FindByEmailAsync(options.Email))!));

        options.Email = Email();
        options.Password = new string('a', 12); // Length is valid, but standard Identity strength validation must fail.
        await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentAccountSeeder.SeedAsync("Development", options, users, context, clock, default));
        await using var verification = Database.CreateContext();
        Assert.False(await verification.Users.AnyAsync(user => user.Email == options.Email));
    }

    [Fact]
    public async Task Duplicate_registration_race_is_resolved_by_unique_database_index()
    {
        await PrepareRoles();
        var gate = new RegistrationGate();
        await using var factory = new AuthApiFactory(Database, registrationGate: gate);
        using var first = factory.Browser();
        using var second = factory.Browser();
        var payload = new { email = Email(), password = Password() };
        var firstToken = await BrowserRequests.TokenAsync(first);
        var secondToken = await BrowserRequests.TokenAsync(second);
        var responses = await Task.WhenAll(BrowserRequests.PostAuthAsync(first, "register", payload, firstToken), BrowserRequests.PostAuthAsync(second, "register", payload, secondToken));
        Assert.Equal(2, gate.Participants);
        Assert.Equal(new[] { 201, 400 }, responses.Select(response => (int)response.StatusCode).Order().ToArray());
        foreach (var response in responses) response.Dispose();
        await using var context = Database.CreateContext();
        Assert.Equal(1, await context.Users.CountAsync(user => user.Email == payload.email));
    }

    [Fact]
    public async Task Failed_login_counter_is_not_lost_across_two_api_hosts()
    {
        await PrepareRoles();
        var gate = new LoginGate();
        await using var firstHost = new AuthApiFactory(Database, loginGate: gate);
        await using var secondHost = new AuthApiFactory(Database, loginGate: gate);
        using var first = firstHost.Browser();
        using var second = secondHost.Browser();
        var email = Email();
        using var registered = await BrowserRequests.PostAuthAsync(first, "register", new { email, password = Password() }, await BrowserRequests.TokenAsync(first));
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var tokenOne = await BrowserRequests.TokenAsync(first);
        var tokenTwo = await BrowserRequests.TokenAsync(second);
        var responses = await Task.WhenAll(
            BrowserRequests.PostAuthAsync(first, "login", new { email, password = Password() }, tokenOne),
            BrowserRequests.PostAuthAsync(second, "login", new { email, password = Password() }, tokenTwo));
        Assert.Equal(2, gate.Participants);
        foreach (var response in responses)
        {
            await Problem(response, 401, "invalid_credentials");
            response.Dispose();
        }

        await using var context = Database.CreateContext();
        Assert.Equal(2, (await context.Users.SingleAsync(user => user.Email == email)).AccessFailedCount);
    }

    private async Task PrepareRoles()
    {
        await using var context = Database.CreateContext();
        await DevelopmentSeeder.SeedRolesAsync(context, default);
    }

    [Fact]
    public async Task Global_csrf_policy_applies_to_controllers_without_auth_annotations()
    {
        await using var factory = new AuthApiFactory(Database);
        using var client = factory.Browser();
        using var denied = await client.PostAsync("/_tests/auth/unsafe", null);
        await Problem(denied, 400, "antiforgery_failed");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_tests/auth/unsafe");
        request.Headers.Add("X-CSRF-TOKEN", await BrowserRequests.TokenAsync(client));
        using var allowed = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    public static async Task<JsonElement> Problem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(Assert.Single(response.Headers.GetValues("X-Trace-Id")), body.GetProperty("traceId").GetString());
        return body;
    }

    public static string Email() => "auth-" + Guid.NewGuid().ToString("N") + "@example.invalid";
    public static string Password() => "Aa1!" + Guid.NewGuid().ToString("N");
}
