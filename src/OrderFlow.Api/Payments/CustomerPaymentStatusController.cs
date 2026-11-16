using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Application.Modules.Payments;

namespace OrderFlow.Api.Payments;

[ApiController, Route("api/orders/{orderId:guid}/payment-status"), Authorize(Policy = AuthorizationPolicies.CustomerAccess)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerPaymentStatusController(CustomerPaymentStatus reads) : ControllerBase
{
    [HttpGet]
    public Task<CustomerPaymentStatusResponse> Get(Guid orderId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) => reads.ReadAsync(orderId, page, pageSize, cancellationToken);
}
