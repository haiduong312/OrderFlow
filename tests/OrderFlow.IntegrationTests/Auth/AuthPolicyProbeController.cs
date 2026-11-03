using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth;

namespace OrderFlow.IntegrationTests.Auth;

[ApiController]
[Route("_tests/auth")]
public sealed class AuthPolicyProbeController : ControllerBase
{
    [HttpGet("admin"), Authorize(Policy = BrowserAuth.AdminPolicy)]
    public IActionResult Admin() => Ok();

    [HttpGet("customer"), Authorize(Policy = BrowserAuth.CustomerPolicy)]
    public IActionResult Customer() => Ok();

    [HttpPost("unsafe")]
    public IActionResult Unsafe() => Ok();
}
