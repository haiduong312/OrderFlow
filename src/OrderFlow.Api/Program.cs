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
using OrderFlow.Application.Modules.Reporting;
using OrderFlow.Application.Observability;
using OpenTelemetry.Trace;
using OrderFlow.Api.Hosting;
using OrderFlow.Application.Hosting;

namespace OrderFlow.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        if (args is ["healthcheck"])
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                client.DefaultRequestHeaders.Host = Environment.GetEnvironmentVariable("Deployment__PublicHost") ?? "localhost";
                using var response = await client.GetAsync("http://127.0.0.1:8080/api/health/ready");
                Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { Environment.ExitCode = 1; }
            return;
        }
        await using var app = Build(args);
        await StartupReadiness.WaitAsync(app.Services, app.Configuration, CancellationToken.None);
        var backgroundServices = app.Services.GetServices<IHostedService>().OfType<BackgroundService>().ToArray();
        await app.RunAsync();
        if (backgroundServices.Any(service => service.ExecuteTask?.IsFaulted == true)) Environment.ExitCode = 1;
    }

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(Program).Assembly.GetName().Name });
        if (Environment.GetEnvironmentVariable("ORDERFLOW_SETTINGS_FILE") is { Length: > 0 } settingsFile)
            builder.Configuration.AddJsonFile(settingsFile, optional: false, reloadOnChange: false).AddEnvironmentVariables().AddCommandLine(args);
        builder.Logging.AddSafeConsole();
        builder.Services.AddHttpFoundations();
        builder.Services.AddOrderFlowPersistence(builder.Configuration).AddSignInManager();
        builder.Services.AddBrowserAuthentication();
        builder.Services.AddCatalog();
        builder.Services.AddInventory();
        builder.Services.AddScoped<EmailAdministration>();
        builder.Services.AddScoped<NotificationService>();
        builder.Services.AddScoped<CheckoutService>();
        builder.Services.AddScoped<OrderReadService>();
        builder.Services.AddScoped<ReportingService>();
        builder.Services.AddScoped<AdminAuditService>();
        builder.Services.AddScoped<OrderLifecycleService>();
        builder.Services.AddScoped<PaymentInitiationService>();
        builder.Services.AddScoped<CustomerPaymentStatus>();
        builder.Services.AddSingleton(_ => PaymentCallbackSettings.Read(builder.Configuration));
        builder.Services.AddScoped<PaymentCallbackReceipt>();
        configure?.Invoke(builder);
        var deployment = builder.Services.AddDeploymentHosting(builder.Configuration);
        builder.Services.AddSingleton<DatabaseReadiness>();
        builder.Services.AddSingleton<IRuntimeReadiness, ApiReadiness>();
        builder.Services.AddOrderFlowTelemetry(builder.Configuration, builder.Logging, "orderflow-api")?
            .WithTracing(traces => traces.AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = false;
                options.Filter = context => !context.Request.Path.StartsWithSegments("/api/health");
            }));
        builder.Services.AddCatalogCaching(builder.Configuration);
        builder.Services.AddNotificationRealtime(builder.Configuration);
        var app = builder.Build();
        // Validate final configuration before accepting any requests, including in test hosts.
        _ = app.Services.GetRequiredService<BrowserAuthSettings>();
        _ = app.Services.GetRequiredService<PaymentCallbackSettings>();
        _ = app.Services.GetRequiredService<CatalogCacheSettings>();
        if (deployment.Enabled)
        {
            app.UseForwardedHeaders(); // Before authentication, origin/HTTPS checks and rate-limit partitioning.
            if (deployment.ExposeInstanceHeader)
                app.Use(async (context, next) =>
                {
                    context.Response.OnStarting(() => { context.Response.Headers["X-OrderFlow-Instance"] = deployment.InstanceName; return Task.CompletedTask; });
                    await next(context);
                });
        }
        app.UseHttpFoundations();
        app.UseNotificationRealtime();

        // Liveness describes this process only, not database or worker readiness.
        app.MapGet("/api/health/live", () => new LivenessResponse("Healthy"))
            .WithName("GetLiveness")
            .WithSummary("Check API process liveness.")
            .WithDescription("Does not check PostgreSQL or worker readiness.")
            .Produces<ApiProblemDetails>(500, "application/problem+json");

        app.MapGet("/api/health/ready", async (IRuntimeReadiness readiness, HttpContext context) =>
        {
            var result = await readiness.CheckAsync(context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new LivenessResponse(result.Ready ? "Healthy" : "Unhealthy"), statusCode: result.Ready ? 200 : 503);
        }).WithName("GetReadiness").WithSummary("Check PostgreSQL connectivity and expected schema; broker/email are not API readiness dependencies.");

        return app;
    }
}

public sealed record LivenessResponse(string Status);
