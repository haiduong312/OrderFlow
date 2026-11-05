using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Api.Http;
using OrderFlow.Application.Modules.Inventory;

namespace OrderFlow.Api.Inventory;

[ApiController, Route("api/admin/products/{productId:guid}"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
[RequestSizeLimit(128 * 1024)]
[ProducesResponseType<ApiProblemDetails>(400, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(401, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(403, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(404, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(409, "application/problem+json")]
public sealed class AdminInventoryController(InventoryService inventory) : ControllerBase
{
    [HttpGet("inventory"), ProducesResponseType<InventoryResponse>(200)]
    public Task<InventoryResponse> Get(Guid productId, CancellationToken cancellationToken) => inventory.GetAsync(productId, cancellationToken);

    [HttpGet("stock-history"), ProducesResponseType<StockHistoryResponse>(200)]
    public Task<StockHistoryResponse> History(Guid productId, [FromQuery] StockHistoryQuery query, CancellationToken cancellationToken) =>
        inventory.HistoryAsync(productId, query, cancellationToken);

    [HttpPost("stock-receipts"), ProducesResponseType<StockMovementResponse>(201)]
    public async Task<ActionResult<StockMovementResponse>> Receive(Guid productId, StockReceiptRequest request, CancellationToken cancellationToken)
    {
        var movement = await inventory.ReceiveAsync(productId, request, cancellationToken);
        return CreatedAtAction(nameof(History), new { productId }, movement);
    }

    [HttpPost("stock-adjustments"), ProducesResponseType<StockMovementResponse>(201)]
    public async Task<ActionResult<StockMovementResponse>> Adjust(Guid productId, StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var movement = await inventory.AdjustAsync(productId, request, cancellationToken);
        return CreatedAtAction(nameof(History), new { productId }, movement);
    }
}
