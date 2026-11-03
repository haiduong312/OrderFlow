using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using OrderFlow.Api.Http;

namespace OrderFlow.Api.Auth;

// JSON-only AddControllers does not register MVC's internal view antiforgery filters.
// Delegate token cryptography/validation to ASP.NET Core without adding a view stack.
public sealed class BrowserAntiforgeryFilter(IAntiforgery antiforgery)
    : IAsyncAuthorizationFilter, IAntiforgeryPolicy, IOrderedFilter
{
    public int Order => 1000;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (!context.IsEffectivePolicy<IAntiforgeryPolicy>(this)
            || HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.Result = new ObjectResult(ApiProblems.Create(context.HttpContext, 400,
                "antiforgery_failed", "A valid antiforgery cookie and request token are required."))
            {
                StatusCode = 400, ContentTypes = { "application/problem+json" }
            };
        }
    }
}
