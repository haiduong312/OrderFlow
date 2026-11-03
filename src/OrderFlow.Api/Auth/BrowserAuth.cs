using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OrderFlow.Api.Http;
using OrderFlow.Api.Auth.Authorization;

namespace OrderFlow.Api.Auth;

public sealed record BrowserAuthSettings(bool AllowInsecureHttp);

public static class BrowserAuth
{
    public const string WriteLimit = "auth-write";
    public const string ReadLimit = "auth-read";
    public const string CsrfHeader = "X-CSRF-TOKEN";

    public static IServiceCollection AddBrowserAuthentication(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var allowHttp = provider.GetRequiredService<IConfiguration>().GetValue<bool>("Authentication:AllowInsecureHttp");
            if (allowHttp && !provider.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
            {
                throw new InvalidOperationException("Authentication:AllowInsecureHttp is permitted only in Development.");
            }

            return new BrowserAuthSettings(allowHttp);
        });
        services.AddScoped<AuthTransportFilter>();
        services.AddScoped<BrowserAntiforgeryFilter>();
        services.AddScoped<AuthSessionService>();
        services.AddDataProtection().SetApplicationName("OrderFlow");
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider, BrowserAuthSettings>((options, clock, settings) =>
            {
                options.Cookie.Name = settings.AllowInsecureHttp ? "OrderFlow.Dev.Auth" : "__Host-OrderFlow.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = settings.AllowInsecureHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.Cookie.Path = "/";
                options.Cookie.Domain = null;
                options.Cookie.IsEssential = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
                options.TimeProvider = clock;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));
        services.AddAntiforgery();
        services.AddOptions<AntiforgeryOptions>().Configure<BrowserAuthSettings>((options, settings) =>
        {
            options.HeaderName = CsrfHeader;
            options.Cookie.Name = settings.AllowInsecureHttp ? "OrderFlow.Dev.Antiforgery" : "__Host-OrderFlow.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = settings.AllowInsecureHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Cookie.Path = "/";
            options.Cookie.IsEssential = true;
        });
        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add(new ServiceFilterAttribute(typeof(BrowserAntiforgeryFilter)) { Order = 1000 });
        });
        services.AddResourceAuthorization();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(WriteLimit, context => Partition(context, 20));
            options.AddPolicy(ReadLimit, context => Partition(context, 60));
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await ApiProblems.WriteAsync(context.HttpContext,
                    ApiProblems.FromStatus(context.HttpContext, 429), cancellationToken);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext context, int limit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
            });
}
