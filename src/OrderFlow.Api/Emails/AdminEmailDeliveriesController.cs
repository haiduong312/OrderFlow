using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Application.Modules.Emails;

namespace OrderFlow.Api.Emails;

[ApiController, Route("api/admin/email-deliveries"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(128 * 1024)]
public sealed class AdminEmailDeliveriesController(EmailAdministration administration) : ControllerBase
{
    [HttpGet]
    public Task<EmailDeliveryPage> Failures(CancellationToken token, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        administration.FailuresAsync(page, pageSize, token);
    [HttpPost("{id:guid}/retry")]
    public Task<EmailDeliveryMetadata> Retry(Guid id, EmailRetryRequest request, CancellationToken token) => administration.RetryAsync(id, request, token);
}
