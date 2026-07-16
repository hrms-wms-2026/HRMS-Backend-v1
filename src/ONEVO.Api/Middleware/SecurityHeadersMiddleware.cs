namespace ONEVO.Api.Middleware;

/// <summary>
/// Adds baseline security response headers to every response. Swagger UI paths get
/// a relaxed CSP because the UI loads its own inline scripts/styles; everything
/// else is an API response and gets a deny-all CSP.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private const string ApiCsp = "default-src 'none'; frame-ancestors 'none'";
    private const string SwaggerCsp =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] =
            context.Request.Path.StartsWithSegments("/swagger") ? SwaggerCsp : ApiCsp;

        return _next(context);
    }
}
