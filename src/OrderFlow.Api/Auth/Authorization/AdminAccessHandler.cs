using Microsoft.AspNetCore.Authorization;
using OrderFlow.Application.Modules.Identity;

namespace OrderFlow.Api.Auth.Authorization;

public sealed class AdminAccessRequirement : IAuthorizationRequirement;

public sealed class AdminAccessHandler : AuthorizationHandler<AdminAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminAccessRequirement requirement)
    {
        if (UserIdentity.Read(context.User) is { } user
            && user.Identity.HasClaim(user.Identity.RoleClaimType, IdentityPolicy.Admin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
