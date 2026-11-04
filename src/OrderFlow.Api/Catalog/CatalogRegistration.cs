using Microsoft.AspNetCore.OpenApi;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using OrderFlow.Application.Modules.Catalog;

namespace OrderFlow.Api.Catalog;

public static class CatalogRegistration
{
    public static IServiceCollection AddCatalog(this IServiceCollection services)
    {
        services.AddScoped<CatalogService>();
        services.Configure<OpenApiOptions>("v1", options => options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;
            if ((type == typeof(CreateProductRequest) || type == typeof(UpdateProductRequest) || type == typeof(ProductResponse))
                && schema.Properties is { } properties)
            {
                // Prices are positive, while the shared money primitive also permits zero.
                // Override only Catalog's property, preserving unrelated money schemas.
                properties["price"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Pattern = "^(?!0(?:\\.0{1,2})?$)(0|[1-9][0-9]{0,15})(\\.[0-9]{1,2})?$",
                    Description = "Positive USD price, 0.01 through 9999999999999999.99. Input accepts 0–2 fractional digits; output always has 2. JSON numbers/null are invalid."
                };
                if (type == typeof(ProductResponse))
                {
                    properties["currency"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String, Enum = [JsonValue.Create("USD")], Description = "Always USD in v1."
                    };
                }
            }
            if ((type == typeof(UpdateProductRequest) || type == typeof(UpdateCategoryRequest) || type == typeof(ArchiveRequest))
                && schema.Properties is { } updateProperties)
            {
                // Nullable CLR fields distinguish omission during binding, but [Required]
                // rejects null. Preserve range metadata while documenting that contract.
                foreach (var name in new[] { "isActive", "expectedVersion" })
                {
                    if (updateProperties.TryGetValue(name, out var property) && property is OpenApiSchema value
                        && value.Type is { } flags)
                    {
                        value.Type = flags & ~JsonSchemaType.Null;
                    }
                }
            }
            return Task.CompletedTask;
        }));
        return services;
    }
}
