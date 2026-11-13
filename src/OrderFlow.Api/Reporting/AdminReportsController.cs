using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Api.Http;
using OrderFlow.Application.Modules.Reporting;

namespace OrderFlow.Api.Reporting;

[ApiController, Route("api/admin/reports"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ApiProblemDetails>(400, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(401, "application/problem+json")]
[ProducesResponseType<ApiProblemDetails>(403, "application/problem+json")]
public sealed class AdminReportsController(ReportingService reports) : ControllerBase
{
    [HttpGet("sales")]
    public Task<SalesReport> Sales([FromQuery] ReportRangeQuery query, CancellationToken token) => reports.SalesAsync(query, token);
    [HttpGet("order-counts")]
    public Task<OrderCountsReport> Counts([FromQuery] ReportRangeQuery query, CancellationToken token) => reports.CountsAsync(query, token);
    [HttpGet("top-products")]
    public Task<TopProductsReport> Top([FromQuery] TopProductsQuery query, CancellationToken token) => reports.TopAsync(query, token);
    [HttpGet("low-stock")]
    public Task<AuditPage<LowStockProduct>> LowStock([FromQuery] LowStockQuery query, CancellationToken token) => reports.LowStockAsync(query, token);
}
