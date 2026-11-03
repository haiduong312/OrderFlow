using Microsoft.AspNetCore.Authorization;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Modules.Identity;

namespace OrderFlow.Api.Auth.Authorization;

public sealed class CustomerOwnershipRequirement : IAuthorizationRequirement;

public sealed class CustomerOwnershipHandler : AuthorizationHandler<CustomerOwnershipRequirement, IOwnedResource>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        CustomerOwnershipRequirement requirement, IOwnedResource resource)
    {
        if (UserIdentity.Read(context.User) is { } user
            && user.Identity.HasClaim(user.Identity.RoleClaimType, IdentityPolicy.Customer)
            && resource.OwnerId == user.Id)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
