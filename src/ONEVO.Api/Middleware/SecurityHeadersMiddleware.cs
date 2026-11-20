using ONEVO.Api.Extensions;

namespace ONEVO.Api.Middleware;

/// <summary>
/// Adds baseline security response headers to every response. API responses get a deny-all CSP
/// (task 23 removed Swagger UI and its relaxed CSP).
///
/// Task 23: API responses also get Permissions-Policy, Cross-Origin-Opener-Policy and
/// Cross-Origin-Resource-Policy. CORP is same-origin here; file downloads relax it to
/// same-site in <see cref="ONEVO.Api.Filters.UserFileResponseFilter"/>, because the web app
/// loads images from the API with &lt;img&gt; tags and locally runs on another port
/// (another origin on the same site). HSTS is added by UseHsts (see Program.cs).
///
/// When the API also hosts the Angular SPAs (<c>Hosting:ServeSpas</c>), every non-API request is an
/// SPA request and gets <see cref="SpaCsp"/> and the base headers only: the API-only policies above
/// would switch off browser features the web app's pages use.
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

    /// <summary>
    /// CSP for the web app and the admin console, the same in both tenancy modes. Built from the
    /// two Angular apps' index.html, angular.json and source (see the Task 4 fix report):
    /// <list type="bullet">
    /// <item>script-src hashes: the web app's inline theme-paint script in src/index.html (LF and
    /// CRLF builds hash differently, so both are listed; recompute if that script changes), plus
    /// Angular's critical-CSS loader <c>onload="this.media='all'"</c>, which needs
    /// 'unsafe-hashes' rather than script 'unsafe-inline'.</item>
    /// <item>style-src 'unsafe-inline': inlined critical CSS, inlined Google Fonts CSS and
    /// Angular/CDK runtime style attributes; fonts.googleapis.com if font inlining is skipped.</item>
    /// <item>font-src fonts.gstatic.com: Google Fonts files referenced by the inlined CSS.</item>
    /// <item>img-src https: blob: data:: OpenStreetMap tiles, uploaded photos, object-URL
    /// previews.</item>
    /// <item>connect-src: same-origin API and SignalR (wss:), and the Nominatim geocoder.</item>
    /// </list>
    /// </summary>
    internal const string SpaCsp =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-hashes' " +
        "'sha256-CVi1UY8C6WnKxHu4Hvazs2qqrmWAlhtaj43RVUC9G9A=' " +
        "'sha256-MVSs35/HSfilKFzAx4Y1NJ8pcRADqmJquXvfq0qL/QQ=' " +
        "'sha256-MhtPZXr7+LpJUY5qtMutB+qWfQtMaPccfe7QXtCcEYc='; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "img-src 'self' data: blob: https:; " +
        "font-src 'self' data: https://fonts.gstatic.com; " +
        "connect-src 'self' wss: https://nominatim.openstreetmap.org; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    private readonly RequestDelegate _next;
    private readonly bool _serveSpas;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _serveSpas = configuration.GetValue<bool>(SpaHostingExtensions.ServeSpasKey);
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        if (_serveSpas && !SpaHostingExtensions.IsApiPath(context.Request.Path))
        {
            headers["Content-Security-Policy"] = SpaCsp;
            return _next(context);
        }

        headers["Content-Security-Policy"] = ApiCsp;
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";

        return _next(context);
    }
}
