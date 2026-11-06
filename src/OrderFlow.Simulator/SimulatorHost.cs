using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Payments.Simulation;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Simulator;

public sealed record ScenarioControl(string Scenario);
public sealed record StateControl(string State);

public static class SimulatorHost
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddOrderFlowPersistence(builder.Configuration);
        builder.Services.AddScoped<SimulatorService>();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<GatewayPaymentState>(allowIntegerValues: false));
        });
        builder.Services.AddProblemDetails();
        configure?.Invoke(builder);
        if ((!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
            || !builder.Configuration.GetValue("Simulator:Enabled", false))
            throw new InvalidOperationException("The simulator requires explicit enablement in Development/Testing; Production is forbidden.");
        var apiKey = Secret(builder.Configuration, "ApiKey");
        var controlKey = Secret(builder.Configuration, "ControlKey");
        var signingKey = Secret(builder.Configuration, "CallbackSigningKey");
        if (apiKey == controlKey || apiKey == signingKey || controlKey == signingKey)
            throw new InvalidOperationException("Use independent simulator API, control and signing secrets.");
        var keyId = builder.Configuration["Simulator:CallbackKeyId"] ?? "development-v1";
        if (keyId.Length is < 1 or > 64 || keyId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("Invalid simulator callback key ID.");
        var urls = builder.Configuration["Urls"] ?? "http://127.0.0.1:5090";
        if (urls.Split(';').Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsLoopback
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidOperationException("The simulator must bind only to loopback URLs.");
        // Disallow alternate endpoint configuration that could bypass the loopback URL restriction.
        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
            throw new InvalidOperationException("Use loopback Urls, not Kestrel endpoint overrides, for the simulator.");
        builder.WebHost.UseUrls(urls).ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
        var app = builder.Build();
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            context.Response.StatusCode = error switch { SimulatorConflictException => 409, ArgumentException or JsonException => 400,
                BadHttpRequestException bad when bad.StatusCode is >= 400 and < 500 => bad.StatusCode, _ => 500 };
            await context.Response.WriteAsJsonAsync(new { code = context.Response.StatusCode switch
                { 409 => "provider_idempotency_conflict", 400 => "invalid_request", _ => "simulator_error" } }, context.RequestAborted);
        }));
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            // Dedicated machine credentials, no cookies, no CORS. Controls never accept merchant credentials.
            var control = context.Request.Path.StartsWithSegments("/control");
            var header = control ? "X-Simulator-Control-Key" : "X-Simulator-Key";
            var expected = control ? controlKey : apiKey;
            if (!context.Request.Headers.TryGetValue(header, out var supplied) || supplied.Count != 1 || supplied[0] is not { Length: <= 512 } value
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(value)), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            { context.Response.StatusCode = 401; return; }
            await next(context);
        });
        app.MapPost("/provider/payments", async (GatewayPaymentRequest request, SimulatorService service, HttpContext context) =>
        {
            var result = await service.SubmitAsync(request, context.RequestAborted);
            if (result.LoseResponse)
            {
                // Durable provider work has committed (or intentionally never started). No DB lock is held.
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
            return Results.Json(result.Result, GatewayJson.Options);
        });
        app.MapGet("/provider/payments/{key}", async (string key, SimulatorService service, CancellationToken token) =>
            await service.LookupAsync(key, token) is { } payment ? Results.Json(payment.View(), GatewayJson.Options) : Results.NotFound());
        app.MapPost("/provider/refunds", async (GatewayRefundRequest request, SimulatorService service, CancellationToken token) =>
            Results.Json(await service.RefundAsync(request, token), GatewayJson.Options));
        app.MapGet("/provider/refunds/{key}", async (string key, SimulatorService service, CancellationToken token) =>
            await service.LookupRefundAsync(key, token) is { } refund ? Results.Json(refund.View(), GatewayJson.Options) : Results.NotFound());
        app.MapPut("/control/payments/{key}/scenario", async (string key, ScenarioControl request, SimulatorService service, CancellationToken token) =>
        {
            if (!Enum.GetNames<SimulatorScenario>().Contains(request.Scenario, StringComparer.Ordinal)) return Results.BadRequest();
            await service.SetScenarioAsync(key, Enum.Parse<SimulatorScenario>(request.Scenario), token); return Results.NoContent();
        });
        app.MapPost("/control/payments/{key}/resolve", async (string key, StateControl request, SimulatorService service, CancellationToken token) =>
        {
            if (!Enum.GetNames<GatewayPaymentState>().Contains(request.State, StringComparer.Ordinal)) return Results.BadRequest();
            return await service.ResolveAsync(key, Enum.Parse<GatewayPaymentState>(request.State), token) ? Results.NoContent() : Results.NotFound();
        });
        app.MapPost("/control/payments/{key}/callback", async (string key, SimulatorService service, TimeProvider clock, CancellationToken token) =>
        {
            var payment = await service.LookupAsync(key, token);
            if (payment is null) return Results.NotFound();
            var body = JsonSerializer.Serialize(new { schemaVersion = 1, provider = "Simulator", providerEventId = payment.EventId,
                occurredAt = payment.UpdatedAt, payment = payment.View() }, GatewayJson.Options);
            return Results.Json(CallbackSigner.Sign(keyId, signingKey, body, BusinessTime.GetUtcNow(clock)));
        });
        return app;
    }

    private static string Secret(IConfiguration configuration, string name) => configuration["Simulator:" + name] is { Length: >= 32 and <= 512 } value
        ? value : throw new InvalidOperationException("Configure a private Simulator:" + name + " of 32–512 characters.");
}
