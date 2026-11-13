using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Api.Http;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Reporting;

namespace OrderFlow.Api.Reporting;

[ApiController, Route("api/admin/audit"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ApiProblemDetails>(400, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(401, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(403, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(404, "application/problem+json")]
public sealed class AdminAuditController(AdminAuditService audit) : ControllerBase
{
    [HttpGet("orders/{orderId:guid}/history")]
    public Task<AuditPage<OrderStatusHistoryResponse>> History(Guid orderId, [FromQuery] AuditQuery query, CancellationToken token) => audit.HistoryAsync(orderId, query, token);
    [HttpGet("orders/{orderId:guid}/payment-attempts")]
    public Task<AuditPage<PaymentAudit>> Payments(Guid orderId, [FromQuery] AuditQuery query, CancellationToken token) => audit.PaymentsAsync(orderId, query, token);
    [HttpGet("orders/{orderId:guid}/captures")]
    public Task<AuditPage<CaptureAudit>> Captures(Guid orderId, [FromQuery] AuditQuery query, CancellationToken token) => audit.CapturesAsync(orderId, query, token);
    [HttpGet("orders/{orderId:guid}/refunds")]
    public Task<AuditPage<RefundAudit>> Refunds(Guid orderId, [FromQuery] AuditQuery query, CancellationToken token) => audit.RefundsAsync(orderId, query, token);
    [HttpGet("stock-movements")]
    public Task<AuditPage<StockMovementResponse>> Stock([FromQuery] StockAuditQuery query, CancellationToken token) => audit.StockAsync(query, token);
}
