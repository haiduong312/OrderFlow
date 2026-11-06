using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Modules.Payments;

namespace OrderFlow.Api.Payments;

[ApiController, Route("api/orders/{orderId:guid}/payment-attempts"), Authorize(Policy = AuthorizationPolicies.CustomerAccess)]
[RequestSizeLimit(128 * 1024)]
public sealed class PaymentAttemptsController(PaymentInitiationService payments) : ControllerBase
{
    [HttpPost, ProducesResponseType<PaymentAttemptResponse>(202)]
    public async Task<IActionResult> Initiate(Guid orderId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] InitiatePaymentRequest? request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1)
            throw new ApplicationFailureException(ApplicationError.ValidationFailed);
        var response = await payments.InitiateAsync(orderId, keys[0], cancellationToken);
        Response.StatusCode = response.StatusCode; Response.Headers.Location = response.Location; Response.Headers.CacheControl = "no-store";
        return File(response.Body, response.ContentType);
    }

    [HttpGet("{attemptId:guid}"), ProducesResponseType<PaymentAttemptResponse>(200)]
    public Task<PaymentAttemptResponse> Get(Guid orderId, Guid attemptId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return payments.GetAsync(orderId, attemptId, cancellationToken);
    }
}
