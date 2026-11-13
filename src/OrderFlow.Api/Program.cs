using OrderFlow.Api.Http;
using OrderFlow.Api.Auth;
using Microsoft.AspNetCore.Identity;
using OrderFlow.Application.Persistence;
using OrderFlow.Api.Catalog;
using OrderFlow.Api.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Api.Payments;
using OrderFlow.Api.Realtime;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Application.Modules.Notifications;

namespace OrderFlow.Api;

public class Program
{
    public static async Task Main(string[] args) => await Build(args).RunAsync();

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(Program).Assembly.GetName().Name });
        builder.Services.AddHttpFoundations();
        builder.Services.AddOrderFlowPersistence(builder.Configuration).AddSignInManager();
        builder.Services.AddBrowserAuthentication();
        builder.Services.AddCatalog();
        builder.Services.AddInventory();
        builder.Services.AddScoped<EmailAdministration>();
        builder.Services.AddScoped<NotificationService>();
        builder.Services.AddScoped<CheckoutService>();
        builder.Services.AddScoped<OrderReadService>();
        builder.Services.AddScoped<OrderLifecycleService>();
        builder.Services.AddScoped<PaymentInitiationService>();
        builder.Services.AddSingleton(_ => PaymentCallbackSettings.Read(builder.Configuration));
        builder.Services.AddScoped<PaymentCallbackReceipt>();
        configure?.Invoke(builder);
        builder.Services.AddCatalogCaching(builder.Configuration);
        builder.Services.AddNotificationRealtime(builder.Configuration);
        var app = builder.Build();
        // Validate final configuration before accepting any requests, including in test hosts.
        _ = app.Services.GetRequiredService<BrowserAuthSettings>();
        _ = app.Services.GetRequiredService<PaymentCallbackSettings>();
        _ = app.Services.GetRequiredService<CatalogCacheSettings>();
        app.UseHttpFoundations();
        app.UseNotificationRealtime();

        // Liveness describes this process only, not database or worker readiness.
        app.MapGet("/api/health/live", () => new LivenessResponse("Healthy"))
            .WithName("GetLiveness")
            .WithSummary("Check API process liveness.")
            .WithDescription("Does not check PostgreSQL or worker readiness.")
            .Produces<ApiProblemDetails>(500, "application/problem+json");

        return app;
    }
}

public sealed record LivenessResponse(string Status);
