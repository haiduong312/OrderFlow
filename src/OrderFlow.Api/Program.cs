using OrderFlow.Api.Http;
using OrderFlow.Api.Auth;
using Microsoft.AspNetCore.Identity;
using OrderFlow.Application.Persistence;
using OrderFlow.Api.Catalog;
using OrderFlow.Api.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Api.Payments;

namespace OrderFlow.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddHttpFoundations();
        builder.Services.AddOrderFlowPersistence(builder.Configuration).AddSignInManager();
        builder.Services.AddBrowserAuthentication();
        builder.Services.AddCatalog();
        builder.Services.AddInventory();
        builder.Services.AddScoped<CheckoutService>();
        builder.Services.AddScoped<OrderReadService>();
        builder.Services.AddScoped<OrderLifecycleService>();
        builder.Services.AddScoped<PaymentInitiationService>();
        builder.Services.AddSingleton(_ => PaymentCallbackSettings.Read(builder.Configuration));
        builder.Services.AddScoped<PaymentCallbackReceipt>();
        var app = builder.Build();
        // Validate final configuration before accepting any requests, including in test hosts.
        _ = app.Services.GetRequiredService<BrowserAuthSettings>();
        _ = app.Services.GetRequiredService<PaymentCallbackSettings>();
        app.UseHttpFoundations();

        // Liveness describes this process only, not database or worker readiness.
        app.MapGet("/api/health/live", () => new LivenessResponse("Healthy"))
            .WithName("GetLiveness")
            .WithSummary("Check API process liveness.")
            .WithDescription("Does not check PostgreSQL or worker readiness.")
            .Produces<ApiProblemDetails>(500, "application/problem+json");

        await app.RunAsync();
    }
}

public sealed record LivenessResponse(string Status);
