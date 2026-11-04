using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Pagination;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Catalog;

public sealed class CatalogService(OrderFlowDbContext database, TimeProvider clock)
{
    private static readonly Expression<Func<Product, ProductResponse>> ProductProjection = product => new(
        product.Id, product.Sku, product.Name, product.Description, product.Price,
        new CategorySummary(product.Category.Id, product.Category.Name, product.Category.IsActive), product.IsActive, product.Version);
    private static readonly Expression<Func<Category, CategoryResponse>> CategoryProjection = category =>
        new(category.Id, category.Name, category.IsActive, category.Version);

    public async Task<CatalogPage<ProductResponse>> ProductsAsync(ProductQuery request, bool admin, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize);
        var pattern = Pattern(request.Q);
        if (request.CategoryId == Guid.Empty) throw CatalogRules.Invalid("categoryId", "Category ID must be a nonempty UUID.");
        var query = database.Products.AsNoTracking();
        if (!admin) query = query.Where(product => product.IsActive && product.Category.IsActive);
        if (request.CategoryId is { } categoryId) query = query.Where(product => product.CategoryId == categoryId);
        if (pattern is not null) query = query.Where(product => EF.Functions.ILike(product.Name, pattern, "\\")
            || EF.Functions.ILike(product.Sku, pattern, "\\"));
        var sorted = request.Sort switch
        {
            "newest" => query.OrderByDescending(product => product.CreatedAt).ThenByDescending(product => product.Id),
            "name" => query.OrderBy(product => product.Name).ThenBy(product => product.Id),
            "price-asc" => query.OrderBy(product => product.Price).ThenBy(product => product.Id),
            "price-desc" => query.OrderByDescending(product => product.Price).ThenByDescending(product => product.Id),
            _ => throw CatalogRules.Invalid("sort", "Supported sorts are newest, name, price-asc and price-desc.")
        };
        var count = await query.CountAsync(cancellationToken);
        var items = await sorted.Skip(page.Offset).Take(page.PageSize).Select(ProductProjection).ToListAsync(cancellationToken);
        return new(items, page.Page, page.PageSize, count);
    }

    public async Task<CatalogPage<CategoryResponse>> CategoriesAsync(CatalogQuery request, bool admin, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize);
        var pattern = Pattern(request.Q);
        var query = database.Categories.AsNoTracking();
        if (!admin) query = query.Where(category => category.IsActive);
        if (pattern is not null) query = query.Where(category => EF.Functions.ILike(category.Name, pattern, "\\"));
        var sorted = request.Sort switch
        {
            "newest" => query.OrderByDescending(category => category.CreatedAt).ThenByDescending(category => category.Id),
            "name" => query.OrderBy(category => category.Name).ThenBy(category => category.Id),
            _ => throw CatalogRules.Invalid("sort", "Supported sorts are newest and name.")
        };
        var count = await query.CountAsync(cancellationToken);
        var items = await sorted.Skip(page.Offset).Take(page.PageSize).Select(CategoryProjection).ToListAsync(cancellationToken);
        return new(items, page.Page, page.PageSize, count);
    }

    public async Task<ProductResponse> ProductAsync(Guid id, bool admin, CancellationToken cancellationToken) =>
        await database.Products.AsNoTracking().Where(product => product.Id == id
            && (admin || (product.IsActive && product.Category.IsActive)))
            .Select(ProductProjection).SingleOrDefaultAsync(cancellationToken)
        ?? throw new ApplicationFailureException(ApplicationError.NotFound);

    public async Task<CategoryResponse> CategoryAsync(Guid id, bool admin, CancellationToken cancellationToken) =>
        await database.Categories.AsNoTracking().Where(category => category.Id == id && (admin || category.IsActive))
            .Select(CategoryProjection).SingleOrDefaultAsync(cancellationToken)
        ?? throw new ApplicationFailureException(ApplicationError.NotFound);

    public async Task<CategoryResponse> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = Category.Create(request.Name, BusinessTime.GetUtcNow(clock));
        database.Categories.Add(category);
        await SaveAsync(cancellationToken);
        return Response(category);
    }

    public async Task<CategoryResponse> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await database.Categories.SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new ApplicationFailureException(ApplicationError.NotFound);
        category.Update(request.Name, request.IsActive ?? throw CatalogRules.Invalid("isActive", "Active flag is required."),
            CatalogRules.Version(request.ExpectedVersion));
        await SaveAsync(cancellationToken);
        return Response(category);
    }

    public async Task<CategoryResponse> ArchiveCategoryAsync(Guid id, ArchiveRequest request, CancellationToken cancellationToken)
    {
        var category = await database.Categories.SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new ApplicationFailureException(ApplicationError.NotFound);
        category.Archive(CatalogRules.Version(request.ExpectedVersion));
        // Mark the concurrency token even for a no-op archive, so concurrent reactivation
        // cannot be silently acknowledged as still archived.
        database.Entry(category).Property(row => row.Version).IsModified = true;
        await SaveAsync(cancellationToken);
        return Response(category);
    }

    public async Task<ProductResponse> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = Product.Create(request.Sku, request.Name, request.Description, request.Price, request.CategoryId, BusinessTime.GetUtcNow(clock));
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var category = await ActiveCategoryAsync(product.CategoryId, cancellationToken);
        database.Products.Add(product);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Response(product, category);
    }

    public async Task<ProductResponse> UpdateProductAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var product = await database.Products.SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new ApplicationFailureException(ApplicationError.NotFound);
        product.Update(request.Name, request.Description, request.Price, request.CategoryId,
            request.IsActive ?? throw CatalogRules.Invalid("isActive", "Active flag is required."), CatalogRules.Version(request.ExpectedVersion));
        var category = await ActiveCategoryAsync(product.CategoryId, cancellationToken);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Response(product, category);
    }

    public async Task<ProductResponse> ArchiveProductAsync(Guid id, ArchiveRequest request, CancellationToken cancellationToken)
    {
        var product = await database.Products.Include(row => row.Category).SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new ApplicationFailureException(ApplicationError.NotFound);
        product.Archive(CatalogRules.Version(request.ExpectedVersion));
        database.Entry(product).Property(row => row.Version).IsModified = true;
        await SaveAsync(cancellationToken);
        return Response(product, product.Category);
    }

    private async Task<Category> ActiveCategoryAsync(Guid id, CancellationToken cancellationToken)
    {
        CatalogRules.CategoryId(id);
        // Serialize assignment against category archival; only database work occurs in this transaction.
        var category = await database.Categories.FromSqlInterpolated($"""
            SELECT * FROM catalog."Categories" WHERE "Id" = {id} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (category is null) throw CatalogRules.Invalid("categoryId", "Choose an existing active category.");
        if (!category.IsActive) throw new ApplicationFailureException(ApplicationError.StateConflict);
        return category;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApplicationFailureException(ApplicationError.VersionConflict);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_Products_Sku" })
        {
            throw new ApplicationFailureException(ApplicationError.SkuConflict);
        }
    }

    private static string? Pattern(string? keyword)
    {
        if (keyword?.Length > 100) throw CatalogRules.Invalid("q", "Search text must not exceed 100 characters.");
        var value = keyword?.Trim();
        return string.IsNullOrEmpty(value) ? null : "%" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
    }

    private static CategoryResponse Response(Category category) => new(category.Id, category.Name, category.IsActive, category.Version);
    private static ProductResponse Response(Product product, Category category) => new(product.Id, product.Sku, product.Name,
        product.Description, product.Price, new(category.Id, category.Name, category.IsActive), product.IsActive, product.Version);
}
