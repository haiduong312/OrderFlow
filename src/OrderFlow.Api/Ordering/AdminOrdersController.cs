using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Api.Http;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Api.Ordering;

[ApiController, Route("api/admin/orders"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
[RequestSizeLimit(128 * 1024)]
[ProducesResponseType<ApiProblemDetails>(400, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(401, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(403, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(404, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(409, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(503, "application/problem+json")]
public sealed class AdminOrdersController(OrderReadService reads, OrderLifecycleService lifecycle) : ControllerBase
{
    [HttpGet, ProducesResponseType<OrderPage>(200)]
    public Task<OrderPage> List([FromQuery] AdminOrderQuery query, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return reads.AdminListAsync(query, cancellationToken);
    }

    [HttpGet("{id:guid}"), ProducesResponseType<OrderDetailResponse>(200)]
    public Task<OrderDetailResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return reads.AdminDetailAsync(id, cancellationToken);
    }

    [HttpPost("{id:guid}/transitions"), ProducesResponseType<OrderDetailResponse>(200)]
    public Task<OrderDetailResponse> Advance(Guid id, OrderTransitionRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return lifecycle.AdvanceAsync(id, request, cancellationToken);
    }
}
