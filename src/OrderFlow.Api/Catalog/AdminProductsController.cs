using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Api.Http;
using OrderFlow.Application.Modules.Catalog;

namespace OrderFlow.Api.Catalog;

[ApiController, Route("api/admin/products"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
[RequestSizeLimit(128 * 1024)]
[ProducesResponseType<ApiProblemDetails>(400, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(401, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(403, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(404, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(409, "application/problem+json")]
public sealed class AdminProductsController(CatalogService catalog) : ControllerBase
{
    [HttpGet, ProducesResponseType<CatalogPage<ProductResponse>>(200)]
    public Task<CatalogPage<ProductResponse>> List([FromQuery] ProductQuery query, CancellationToken cancellationToken) =>
        catalog.ProductsAsync(query, admin: true, cancellationToken);

    [HttpGet("{id:guid}"), ProducesResponseType<ProductResponse>(200)]
    public Task<ProductResponse> Get(Guid id, CancellationToken cancellationToken) => catalog.ProductAsync(id, admin: true, cancellationToken);

    [HttpPost, ProducesResponseType<ProductResponse>(201)]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await catalog.CreateProductAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}"), ProducesResponseType<ProductResponse>(200)]
    public Task<ProductResponse> Update(Guid id, UpdateProductRequest request, CancellationToken cancellationToken) => catalog.UpdateProductAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/archive"), ProducesResponseType<ProductResponse>(200)]
    public Task<ProductResponse> Archive(Guid id, ArchiveRequest request, CancellationToken cancellationToken) => catalog.ArchiveProductAsync(id, request, cancellationToken);
}
