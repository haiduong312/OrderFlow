using Microsoft.AspNetCore.Identity;

namespace OrderFlow.Application.Modules.Identity;

public static class IdentityPolicy
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";

    public static void Configure(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        // Email verification is outside v1; the browser cannot set confirmation flags.
        options.SignIn.RequireConfirmedEmail = false;
    }
}
