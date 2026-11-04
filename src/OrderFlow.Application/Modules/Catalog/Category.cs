using OrderFlow.Application.Common.Time;

namespace OrderFlow.Application.Modules.Catalog;

public sealed class Category
{
    private Category() { }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Category Create(string name, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), Name = CatalogRules.Name(name), IsActive = true, Version = 1,
        CreatedAt = BusinessTime.ToUtcMicroseconds(now)
    };

    public void Update(string name, bool isActive, int expectedVersion)
    {
        CatalogRules.CheckVersion(Version, expectedVersion);
        var validated = CatalogRules.Name(name);
        var next = CatalogRules.NextVersion(Version);
        Name = validated;
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
