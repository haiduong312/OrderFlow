using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderFlow.Application.Common.Errors;

namespace OrderFlow.Application.Modules.Ordering;

public sealed record CheckoutItem(Guid ProductId, int Quantity);

public sealed record CheckoutRequest(IReadOnlyList<CheckoutItem> Items, string CanonicalBody, byte[] Hash)
{
    public const string Operation = "CreateOrder:v1";

    public static string ValidateKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128 || key.Any(character => character is < '!' or > '~'))
            throw Invalid("Idempotency-Key", "Supply one case-sensitive key of 1–128 visible ASCII characters without spaces.");
        return key;
    }

    public static CheckoutRequest Normalize(CreateOrderRequest request)
    {
        if (request.Items is null || request.Items.Count is < 1 or > 100)
            throw Invalid("items", "Choose 1–100 distinct products.");
        var items = new List<CheckoutItem>();
        var seen = new HashSet<Guid>();
        foreach (var item in request.Items)
        {
            if (item?.ProductId is not { } id || id == Guid.Empty)
                throw Invalid("items", "Every item requires a nonempty product UUID.");
            if (item.Quantity is not (>= 1 and <= 1000))
                throw Invalid("items", "Every quantity must be an integer from 1 to 1000.");
            if (!seen.Add(id)) throw Invalid("items", "Duplicate product IDs are not accepted.");
            items.Add(new(id, item.Quantity.Value));
        }
        var sorted = items.OrderBy(item => item.ProductId.ToString("N"), StringComparer.Ordinal).ToArray();
        // Fixed version/property order, canonical lower-case UUIDs and integers. No catalog facts
        // participate in the request hash: later prices/activity do not invalidate a replay.
        var canonical = JsonSerializer.Serialize(new
        {
            version = 1,
            items = sorted.Select(item => new { productId = item.ProductId.ToString("D"), quantity = item.Quantity })
        });
        return new(sorted, canonical, SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static ApplicationFailureException Invalid(string field, string message) =>
        new(ApplicationError.ValidationFailed, new Dictionary<string, string[]> { [field] = [message] });
}
