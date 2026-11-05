using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Api.Http;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Api.Ordering;

[ApiController, Route("api/orders"), Authorize(Policy = AuthorizationPolicies.CustomerAccess)]
[RequestSizeLimit(128 * 1024)]
[ProducesResponseType<ApiProblemDetails>(400, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(401, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(403, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(404, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(409, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(503, "application/problem+json")]
public sealed class OrdersController(CheckoutService checkout) : ControllerBase
{
    [HttpPost, ProducesResponseType<OrderResponse>(201)]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1)
            throw new ApplicationFailureException(ApplicationError.ValidationFailed,
                new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Supply exactly one Idempotency-Key header."] });
        var response = await checkout.CreateAsync(keys[0], request, cancellationToken);
        Response.StatusCode = response.StatusCode;
        Response.Headers.Location = response.Location;
        Response.Headers.CacheControl = "no-store";
        return File(response.Body, response.ContentType); // Write the stored bytes, including on the first response.
    }

    [HttpGet("{id:guid}"), ProducesResponseType<OrderResponse>(200)]
    public async Task<OrderResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return await checkout.GetAsync(id, cancellationToken);
    }
}
