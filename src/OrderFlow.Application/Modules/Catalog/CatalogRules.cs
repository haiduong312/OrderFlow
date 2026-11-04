using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Money;

namespace OrderFlow.Application.Modules.Catalog;

public static class CatalogRules
{
    public static string Name(string? name)
    {
        var value = name?.Trim();
        return !string.IsNullOrEmpty(value) && value.Length <= 200
            ? value : throw Invalid("name", "Name must contain 1–200 characters after trimming.");
    }

    public static string Sku(string? sku)
    {
        var value = sku?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 64
            || value.Any(character => character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-')))
        {
            throw Invalid("sku", "SKU must contain 1–64 ASCII letters, digits or hyphens.");
        }

        return value.ToUpperInvariant();
    }

    public static string Description(string? description)
    {
        var value = description?.Trim() ?? "";
        return value.Length <= 2000 ? value : throw Invalid("description", "Description must not exceed 2000 characters.");
    }

    public static UsdMoney Price(UsdMoney? price) => price is { Value: > 0 } value
        ? value : throw Invalid("price", "A positive USD price is required.");

    public static Guid CategoryId(Guid id) => id != Guid.Empty ? id : throw Invalid("categoryId", "A category ID is required.");

    public static int Version(int? version) => version is > 0 ? version.Value
        : throw Invalid("expectedVersion", "A positive expected version is required.");

    public static void CheckVersion(int current, int expected)
    {
        Version(expected);
        if (current != expected) throw new ApplicationFailureException(ApplicationError.VersionConflict);
    }

    public static int NextVersion(int version) => version < int.MaxValue ? version + 1
        : throw new ApplicationFailureException(ApplicationError.StateConflict);

    public static ApplicationFailureException Invalid(string field, string message) =>
        new(ApplicationError.ValidationFailed, new Dictionary<string, string[]> { [field] = [message] });
}
