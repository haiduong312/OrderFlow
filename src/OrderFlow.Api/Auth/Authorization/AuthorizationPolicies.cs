using Microsoft.AspNetCore.Authorization;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Modules.Identity;

namespace OrderFlow.Api.Auth.Authorization;

public static class AuthorizationPolicies
{
    // Preserve the role policy names introduced with browser authentication.
    public const string AdminAccess = "AdminOnly";
    public const string CustomerAccess = "CustomerOnly";
    public const string CustomerOwnership = "CustomerOwnership";

    public static IServiceCollection AddResourceAuthorization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ResourceAccess>();
        services.AddSingleton<IAuthorizationHandler, CustomerOwnershipHandler>();
        services.AddSingleton<IAuthorizationHandler, AdminAccessHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(CustomerAccess, policy => policy.RequireAuthenticatedUser().RequireRole(IdentityPolicy.Customer));
            options.AddPolicy(CustomerOwnership, policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new CustomerOwnershipRequirement()));
            options.AddPolicy(AdminAccess, policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new AdminAccessRequirement()));
        });
        return services;
    }
}
