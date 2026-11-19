using ONEVO.Api.Extensions;

namespace ONEVO.Api.Middleware;

/// <summary>
/// Adds baseline security response headers to every response. Swagger UI paths get
/// a relaxed CSP because the UI loads its own inline scripts/styles; API responses get a
/// deny-all CSP. When the API also hosts the Angular SPAs (<c>Hosting:ServeSpas</c>), every
/// non-API, non-Swagger request is an SPA request and gets <see cref="SpaCsp"/> instead.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private const string ApiCsp = "default-src 'none'; frame-ancestors 'none'";
    private const string SwaggerCsp =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:";

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
        headers["Content-Security-Policy"] = SelectCsp(context.Request.Path);

        return _next(context);
    }

    private string SelectCsp(PathString path)
    {
        if (path.StartsWithSegments("/swagger"))
            return SwaggerCsp;

        if (_serveSpas && !SpaHostingExtensions.IsApiPath(path))
            return SpaCsp;

        return ApiCsp;
    }
}
