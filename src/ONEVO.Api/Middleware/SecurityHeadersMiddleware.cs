namespace ONEVO.Api.Middleware;

/// <summary>
/// Adds baseline security response headers to every response. Every response is an API
/// response and gets a deny-all CSP (task 23 removed Swagger UI and its relaxed CSP).
///
/// Task 23: also sends Permissions-Policy, Cross-Origin-Opener-Policy and
/// Cross-Origin-Resource-Policy. CORP is same-origin here; file downloads relax it to
/// same-site in <see cref="ONEVO.Api.Filters.UserFileResponseFilter"/>, because the web app
/// loads images from the API with &lt;img&gt; tags and locally runs on another port
/// (another origin on the same site). HSTS is added by UseHsts (see Program.cs).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private const string ApiCsp = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// The API never needs any powerful browser feature, so every one is switched off for
    /// documents served from this origin. This does not affect the web app's own pages, which
    /// are served from the frontend and set their own policy.
    /// </summary>
    public const string PermissionsPolicy =
        "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), " +
        "fullscreen=(), geolocation=(), gyroscope=(), hid=(), magnetometer=(), microphone=(), " +
        "midi=(), payment=(), picture-in-picture=(), publickey-credentials-get=(), " +
        "screen-wake-lock=(), serial=(), usb=(), xr-spatial-tracking=()";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = ApiCsp;
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";

        return _next(context);
    }
}
