using Microsoft.AspNetCore.Antiforgery;
using OrderFlow.Api.Auth;
using OrderFlow.Api.Http;

namespace OrderFlow.Api.Realtime;

public sealed class RealtimeRequestGuard(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, BrowserAuthSettings settings, IAntiforgery antiforgery)
    {
        if (!context.Request.Path.StartsWithSegments(RealtimeRegistration.HubPath)) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.IsHttps && !settings.AllowInsecureHttp)
        { await Reject(context, 400, "https_required", "Realtime requires HTTPS."); return; }
        if (context.Request.Query.ContainsKey("access_token"))
        { await Reject(context, 400, "cookie_authentication_required", "Use the existing browser session."); return; }
        // CORS alone does not protect WebSocket handshakes. Browsers send Origin on WS requests.
        if (context.Request.Headers.TryGetValue("Origin", out var origins)
            && (origins.Count != 1 || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
                || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment)
                || !string.IsNullOrEmpty(origin.UserInfo) || !string.Equals(origin.GetLeftPart(UriPartial.Authority),
                    context.Request.Scheme + "://" + context.Request.Host, StringComparison.OrdinalIgnoreCase)))
        { await Reject(context, 403, "realtime_origin_rejected", "Use the same browser origin."); return; }
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            { await Reject(context, 400, "antiforgery_failed", "Supply the browser antiforgery cookie and request token."); return; }
        }
        await next(context);
    }
    private static Task Reject(HttpContext context, int status, string code, string detail) =>
        ApiProblems.WriteAsync(context, ApiProblems.Create(context, status, code, detail), context.RequestAborted);
}
