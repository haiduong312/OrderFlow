using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OrderFlow.Api.Http;
using OrderFlow.Application.Modules.Identity;

namespace OrderFlow.Api.Auth;

[ApiController]
[Route("api/auth")]
[ServiceFilter(typeof(AuthTransportFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(128 * 1024)]
public sealed class AuthController(AuthSessionService sessions, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting(BrowserAuth.WriteLimit)]
    [ProducesResponseType<UserResponse>(201)]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        Created("/api/auth/me", await sessions.RegisterAsync(request, cancellationToken));

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting(BrowserAuth.WriteLimit)]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await sessions.LoginAsync(request, cancellationToken);
        if (user is null)
        {
            return new ObjectResult(ApiProblems.Create(HttpContext, 401, "invalid_credentials",
                "The credentials are invalid or the account is unavailable."))
            {
                StatusCode = 401, ContentTypes = { "application/problem+json" }
            };
        }

        return Ok(user);
    }

    [HttpPost("logout"), Authorize, EnableRateLimiting(BrowserAuth.WriteLimit)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me"), Authorize, EnableRateLimiting(BrowserAuth.ReadLimit)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(await sessions.DescribeAsync(user, cancellationToken));
    }
}
