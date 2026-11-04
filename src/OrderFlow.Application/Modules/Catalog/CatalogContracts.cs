using System.ComponentModel.DataAnnotations;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Pagination;

namespace OrderFlow.Application.Modules.Catalog;

public sealed record CategoryResponse(Guid Id, string Name, bool IsActive, int Version);
public sealed record CategorySummary(Guid Id, string Name, bool IsActive);
public sealed record ProductResponse(Guid Id, string Sku, string Name, string Description, UsdMoney Price,
    CategorySummary Category, bool IsActive, int Version)
{
    public string Currency => UsdMoney.Currency;
}
public sealed record CatalogPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed class CreateCategoryRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
}
public sealed class UpdateCategoryRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [Required] public bool? IsActive { get; init; }
    [Required, Range(1, int.MaxValue)] public int? ExpectedVersion { get; init; }
}
public sealed class ArchiveRequest
{
    [Required, Range(1, int.MaxValue)] public int? ExpectedVersion { get; init; }
}
public sealed class CreateProductRequest
{
    [Required, StringLength(64)] public string Sku { get; init; } = "";
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
    [Required] public UsdMoney Price { get; init; }
    [Required] public Guid CategoryId { get; init; }
}
public sealed class UpdateProductRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
    [Required] public UsdMoney Price { get; init; }
    [Required] public Guid CategoryId { get; init; }
    [Required] public bool? IsActive { get; init; }
    [Required, Range(1, int.MaxValue)] public int? ExpectedVersion { get; init; }
}

public sealed class CatalogQuery
{
    [StringLength(100)] public string? Q { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = PageRequest.DefaultPage;
    [Range(1, PageRequest.MaximumPageSize)] public int PageSize { get; init; } = PageRequest.DefaultPageSize;
    [RegularExpression("^(newest|name)$")] public string Sort { get; init; } = "newest";
}
public sealed class ProductQuery
{
    [StringLength(100)] public string? Q { get; init; }
    public Guid? CategoryId { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = PageRequest.DefaultPage;
    [Range(1, PageRequest.MaximumPageSize)] public int PageSize { get; init; } = PageRequest.DefaultPageSize;
    [RegularExpression("^(newest|name|price-asc|price-desc)$")] public string Sort { get; init; } = "newest";
}
