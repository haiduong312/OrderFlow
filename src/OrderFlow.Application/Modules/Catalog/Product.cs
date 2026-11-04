using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Catalog;

public sealed class Product
{
    private Product() { }
    public Guid Id { get; private set; }
    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public UsdMoney Price { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Product Create(string sku, string name, string? description, UsdMoney? price,
        Guid categoryId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), Sku = CatalogRules.Sku(sku), Name = CatalogRules.Name(name),
        Description = CatalogRules.Description(description), Price = CatalogRules.Price(price),
        CategoryId = CatalogRules.CategoryId(categoryId), IsActive = true, Version = 1,
        CreatedAt = BusinessTime.ToUtcMicroseconds(now)
    };

    public void Update(string name, string? description, UsdMoney? price, Guid categoryId, bool isActive, int expectedVersion)
    {
        CatalogRules.CheckVersion(Version, expectedVersion);
        var validatedName = CatalogRules.Name(name);
        var validatedDescription = CatalogRules.Description(description);
        var validatedPrice = CatalogRules.Price(price);
        var validatedCategory = CatalogRules.CategoryId(categoryId);
        var next = CatalogRules.NextVersion(Version);
        Name = validatedName;
        Description = validatedDescription;
        Price = validatedPrice;
        CategoryId = validatedCategory;
        IsActive = isActive;
        Version = next;
    }

    public void Archive(int expectedVersion)
    {
        CatalogRules.CheckVersion(Version, expectedVersion);
        if (!IsActive) return;
        Version = CatalogRules.NextVersion(Version);
        IsActive = false;
    }
}
