using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using OrderFlow.Application.Modules.Inventory;

namespace OrderFlow.Api.Inventory;

public static class InventoryRegistration
{
    public static IServiceCollection AddInventory(this IServiceCollection services)
    {
        services.AddScoped<InventoryService>();
        services.Configure<OpenApiOptions>("v1", options => options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;
            if (type == typeof(StockMovementResponse) && schema.Properties is { } movementProperties)
            {
                movementProperties["occurredAt"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String, Format = "date-time",
                    Description = "UTC with Z and exactly six fractional digits."
                };
            }
            if ((type == typeof(StockReceiptRequest) || type == typeof(StockAdjustmentRequest)) && schema.Properties is { } properties)
            {
                foreach (var name in new[] { "quantity", "delta" })
                {
                    if (properties.TryGetValue(name, out var property) && property is OpenApiSchema value && value.Type is { } flags)
                        value.Type = flags & ~JsonSchemaType.Null;
                }
            }
            return Task.CompletedTask;
        }));
        return services;
    }
}
