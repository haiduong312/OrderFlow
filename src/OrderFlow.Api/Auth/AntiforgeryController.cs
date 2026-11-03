using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace OrderFlow.Api.Auth;

// Token issuance does not resolve a database context or require PostgreSQL availability.
[ApiController]
[Route("api/auth/antiforgery")]
[ServiceFilter(typeof(AuthTransportFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AntiforgeryController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet, AllowAnonymous, EnableRateLimiting(BrowserAuth.ReadLimit)]
    public ActionResult<AntiforgeryResponse> Get()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return new AntiforgeryResponse(tokens.RequestToken!, BrowserAuth.CsrfHeader);
    }
}
