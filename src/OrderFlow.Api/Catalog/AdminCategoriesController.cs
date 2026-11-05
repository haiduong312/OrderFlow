using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Api.Http;
using OrderFlow.Application.Modules.Catalog;

namespace OrderFlow.Api.Catalog;

[ApiController, Route("api/admin/categories"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
[RequestSizeLimit(128 * 1024)]
[ProducesResponseType<ApiProblemDetails>(400, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(401, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(403, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(404, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(409, "application/problem+json")]
public sealed class AdminCategoriesController(CatalogService catalog) : ControllerBase
{
    [HttpGet, ProducesResponseType<CatalogPage<CategoryResponse>>(200)]
    public Task<CatalogPage<CategoryResponse>> List([FromQuery] CatalogQuery query, CancellationToken cancellationToken) => catalog.CategoriesAsync(query, admin: true, cancellationToken);

    [HttpGet("{id:guid}"), ProducesResponseType<CategoryResponse>(200)]
    public Task<CategoryResponse> Get(Guid id, CancellationToken cancellationToken) => catalog.CategoryAsync(id, admin: true, cancellationToken);

    [HttpPost, ProducesResponseType<CategoryResponse>(201)]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await catalog.CreateCategoryAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }

    [HttpPut("{id:guid}"), ProducesResponseType<CategoryResponse>(200)]
    public Task<CategoryResponse> Update(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken) => catalog.UpdateCategoryAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/archive"), ProducesResponseType<CategoryResponse>(200)]
    public Task<CategoryResponse> Archive(Guid id, ArchiveRequest request, CancellationToken cancellationToken) => catalog.ArchiveCategoryAsync(id, request, cancellationToken);
}
