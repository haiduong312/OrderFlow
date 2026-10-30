using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.OpenApi;
using OrderFlow.Application.Common.Money;

namespace OrderFlow.Api.Http;

public static class HttpFoundations
{
    public static IServiceCollection AddHttpFoundations(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));
        services.AddControllers(options =>
            options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
            .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = ApiProblems.InvalidModelState;
            // Bodyless controller errors pass through the same status-code-pages contract.
            options.SuppressMapClientErrors = true;
        });

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "OrderFlow API";
                document.Info.Version = "v1";
                document.Info.Description = "Process liveness and shared HTTP foundations. Errors use application/problem+json with code, traceId and errors. See docs/http-contract.md for status mappings. No CRUD/authentication is implemented.";
                return Task.CompletedTask;
            });
            options.AddSchemaTransformer((schema, context, _) =>
            {
                var type = context.JsonTypeInfo.Type;
                if (type == typeof(UsdMoney) || Nullable.GetUnderlyingType(type) == typeof(UsdMoney))
                {
                    schema.Type = type == typeof(UsdMoney)
                        ? JsonSchemaType.String
                        : JsonSchemaType.String | JsonSchemaType.Null;
                    schema.Format = null;
                    schema.Properties?.Clear();
                    schema.Required?.Clear();
                    schema.Pattern = "^(0|[1-9][0-9]{0,15})(\\.[0-9]{1,2})?$";
                    schema.Description = "Nonnegative USD money, maximum 9999999999999999.99. Input accepts 0-2 fractional digits; output always has exactly 2. Prices must additionally be positive.";
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }

    public static WebApplication UseHttpFoundations(this WebApplication app)
    {
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseExceptionHandler(new ExceptionHandlerOptions { AllowStatusCode404Response = true });
        app.UseStatusCodePages(async statusContext =>
        {
            var context = statusContext.HttpContext;
            if (!context.RequestAborted.IsCancellationRequested)
            {
                await ApiProblems.WriteAsync(context,
                    ApiProblems.FromStatus(context, context.Response.StatusCode), context.RequestAborted);
            }
        });
        app.MapControllers();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        return app;
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.Converters.Add(new UsdMoneyJsonConverter());
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.NumberHandling = JsonNumberHandling.Strict;
    }
}
