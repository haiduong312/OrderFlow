using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Http;
using OrderFlow.Application.Modules.Catalog;

namespace OrderFlow.Api.Catalog;

[ApiController, AllowAnonymous, Route("api")]
[ProducesResponseType<ApiProblemDetails>(400, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(404, "application/problem+json")]
public sealed class PublicCatalogController(CatalogService catalog) : ControllerBase
{
    [HttpGet("products")]
    public Task<CatalogPage<ProductResponse>> Products([FromQuery] ProductQuery query, CancellationToken cancellationToken) =>
        catalog.ProductsAsync(query, admin: false, cancellationToken);

    [HttpGet("products/{id:guid}")]
    public Task<ProductResponse> Product(Guid id, CancellationToken cancellationToken) => catalog.ProductAsync(id, admin: false, cancellationToken);

    [HttpGet("categories")]
    public Task<CatalogPage<CategoryResponse>> Categories([FromQuery] CatalogQuery query, CancellationToken cancellationToken) =>
        catalog.CategoriesAsync(query, admin: false, cancellationToken);

    [HttpGet("categories/{id:guid}")]
    public Task<CategoryResponse> Category(Guid id, CancellationToken cancellationToken) => catalog.CategoryAsync(id, admin: false, cancellationToken);
}
