using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OrderFlow.Api.Http;

namespace OrderFlow.Api.Auth;

public sealed class AuthTransportFilter(BrowserAuthSettings settings) : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.Request.IsHttps && !settings.AllowInsecureHttp)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.Result = new ObjectResult(ApiProblems.Create(context.HttpContext, 400,
                "https_required", "Authentication requires HTTPS."))
            {
                StatusCode = 400, ContentTypes = { "application/problem+json" }
            };
        }

        return Task.CompletedTask;
    }
}
