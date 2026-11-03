using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using Xunit;

namespace OrderFlow.IntegrationTests.Authorization;

public sealed class AuthorizationPolicyTests
{
    [Theory]
    [InlineData("Customer", true, true, false)]
    [InlineData("Customer", false, false, false)]
    [InlineData("Admin", true, false, true)]
    [InlineData("Admin", false, false, true)]
    [InlineData("Admin,Customer", true, true, true)]
    [InlineData("Admin,Customer", false, false, true)]
    [InlineData("", true, false, false)]
    public async Task Policies_keep_customer_ownership_and_admin_permission_separate(
        string roles, bool owns, bool customerAllowed, bool adminAllowed)
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var userId = Guid.NewGuid();
        var principal = Principal(userId.ToString(), roles);
        var resource = new OwnedSample(owns ? userId : Guid.NewGuid());
        Assert.Equal(customerAllowed, (await authorization.AuthorizeAsync(principal, resource, AuthorizationPolicies.CustomerOwnership)).Succeeded);
        Assert.Equal(adminAllowed, (await authorization.AuthorizeAsync(principal, resource, AuthorizationPolicies.AdminAccess)).Succeeded);
        // Resource-free permission is useful for future admin create/list operations.
        Assert.Equal(adminAllowed, (await authorization.AuthorizeAsync(principal, null, AuthorizationPolicies.AdminAccess)).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Missing_or_invalid_ids_never_grant_resource_or_admin_access(string? id)
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var principal = Principal(id, "Admin,Customer");
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        Assert.False((await authorization.AuthorizeAsync(principal, new OwnedSample(Guid.Empty), AuthorizationPolicies.CustomerOwnership)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(principal, null, AuthorizationPolicies.AdminAccess)).Succeeded);
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = principal };
        Assert.Null(scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId);
    }

    [Fact]
    public async Task Anonymous_claims_and_roles_from_an_unauthenticated_identity_do_not_grant_access()
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var id = Guid.NewGuid();
        var anonymous = Principal(id.ToString(), "Admin,Customer", authenticated: false);
        Assert.False((await authorization.AuthorizeAsync(anonymous, new OwnedSample(id), AuthorizationPolicies.CustomerOwnership)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(anonymous, null, AuthorizationPolicies.AdminAccess)).Succeeded);

        var mixed = Principal(id.ToString(), "");
        mixed.AddIdentity((ClaimsIdentity)anonymous.Identity!);
        Assert.False((await authorization.AuthorizeAsync(mixed, new OwnedSample(id), AuthorizationPolicies.CustomerOwnership)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(mixed, null, AuthorizationPolicies.AdminAccess)).Succeeded);
    }

    [Fact]
    public async Task Missing_wrong_type_and_ambiguous_resources_or_identities_fail_closed()
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var id = Guid.NewGuid();
        var principal = Principal(id.ToString(), "Admin,Customer");
        Assert.False((await authorization.AuthorizeAsync(principal, null, AuthorizationPolicies.CustomerOwnership)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(principal, new object(), AuthorizationPolicies.CustomerOwnership)).Succeeded);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        Assert.False((await authorization.AuthorizeAsync(principal, new OwnedSample(id), AuthorizationPolicies.CustomerOwnership)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(principal, null, AuthorizationPolicies.AdminAccess)).Succeeded);
        principal = Principal(id.ToString(), "Admin,Customer");
        principal.AddIdentity((ClaimsIdentity)Principal(Guid.NewGuid().ToString(), "Admin").Identity!);
        Assert.False((await authorization.AuthorizeAsync(principal, null, AuthorizationPolicies.AdminAccess)).Succeeded);
    }

    [Fact]
    public void Current_user_reads_only_the_authenticated_context_and_has_no_background_identity()
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        accessor.HttpContext = null;
        Assert.Null(user.UserId);
        var id = Guid.NewGuid();
        accessor.HttpContext = new DefaultHttpContext { User = Principal(id.ToString(), "Customer", authenticated: false) };
        Assert.Null(user.UserId);
        accessor.HttpContext.User = Principal(id.ToString(), "Customer");
        Assert.Equal(id, user.UserId);
        accessor.HttpContext = null;
        Assert.Null(user.UserId);
    }

    [Fact]
    public async Task Access_adapter_checks_authentication_before_existence_and_honors_cancellation()
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<ResourceAccess>();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = null;
        var anonymous = await Assert.ThrowsAsync<ApplicationFailureException>(() => access.RequireCustomerOwnershipAsync(null, default));
        Assert.Equal(ApplicationError.Unauthenticated, anonymous.Error);
        accessor.HttpContext = new DefaultHttpContext { User = Principal(Guid.NewGuid().ToString(), "Customer") };
        var forbidden = await Assert.ThrowsAsync<ApplicationFailureException>(() => access.RequireAdminResourceAsync(null, default));
        Assert.Equal(ApplicationError.Forbidden, forbidden.Error);
        var missing = await Assert.ThrowsAsync<ApplicationFailureException>(() => access.RequireCustomerOwnershipAsync(null, default));
        Assert.Equal(ApplicationError.NotFound, missing.Error);
        accessor.HttpContext.User = Principal(Guid.NewGuid().ToString(), "Admin");
        await access.RequireAdminAsync(default);
        var adminMissing = await Assert.ThrowsAsync<ApplicationFailureException>(() => access.RequireAdminResourceAsync(null, default));
        Assert.Equal(ApplicationError.NotFound, adminMissing.Error);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => access.RequireAdminAsync(cancellation.Token));
    }

    private static ServiceProvider Services() => new ServiceCollection().AddLogging().AddResourceAuthorization()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static ClaimsPrincipal Principal(string? id, string roles, bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (id is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        claims.AddRange(roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "PolicyTest" : null));
    }

    private sealed record OwnedSample(Guid OwnerId) : IOwnedResource;
}
