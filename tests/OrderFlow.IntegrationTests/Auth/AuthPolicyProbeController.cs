using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth;
using OrderFlow.Api.Auth.Authorization;

namespace OrderFlow.IntegrationTests.Auth;

[ApiController]
[Route("_tests/auth")]
public sealed class AuthPolicyProbeController : ControllerBase
{
    [HttpGet("admin"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
    public IActionResult Admin() => Ok();

    [HttpGet("customer"), Authorize(Policy = AuthorizationPolicies.CustomerAccess)]
    public IActionResult Customer() => Ok();

    [HttpPost("unsafe")]
    public IActionResult Unsafe() => Ok();
}
