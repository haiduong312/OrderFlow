using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;

namespace OrderFlow.Api.Auth.Authorization;

// HTTP/application boundary: named policies decide permission; this adapter applies
// the API's concealment contract. It never decides whether a business transition is valid.
public sealed class ResourceAccess(IAuthorizationService authorization, IHttpContextAccessor accessor)
{
    public async Task RequireCustomerOwnershipAsync(IOwnedResource? resource, CancellationToken cancellationToken)
    {
        var user = AuthenticatedCaller(cancellationToken);
        await RequirePolicyAsync(user, null, AuthorizationPolicies.CustomerAccess, ApplicationError.Forbidden, cancellationToken);
        await RequirePolicyAsync(user, resource, AuthorizationPolicies.CustomerOwnership, ApplicationError.NotFound, cancellationToken);
    }

    public async Task RequireAdminAsync(CancellationToken cancellationToken)
    {
        var user = AuthenticatedCaller(cancellationToken);
        await RequirePolicyAsync(user, null, AuthorizationPolicies.AdminAccess, ApplicationError.Forbidden, cancellationToken);
    }

    public async Task RequireAdminResourceAsync(object? resource, CancellationToken cancellationToken)
    {
        // Check privilege first: non-admin callers get the same denial for any ID.
        await RequireAdminAsync(cancellationToken);
        if (resource is null)
        {
            throw new ApplicationFailureException(ApplicationError.NotFound);
        }
    }

    private ClaimsPrincipal AuthenticatedCaller(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        return user?.Identities.Any(identity => identity.IsAuthenticated) == true
            ? user
            : throw new ApplicationFailureException(ApplicationError.Unauthenticated);
    }

    private async Task RequirePolicyAsync(ClaimsPrincipal user, object? resource, string policy,
        ApplicationError denial, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await authorization.AuthorizeAsync(user, resource, policy);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Succeeded)
        {
            throw new ApplicationFailureException(denial);
        }
    }
}
