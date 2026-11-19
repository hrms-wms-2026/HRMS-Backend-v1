using Microsoft.Extensions.FileProviders;
using ONEVO.Api.Middleware;
using ONEVO.Application.Common.Tenancy;

namespace ONEVO.Api.Extensions;

/// <summary>
/// Optional hosting of the two Angular SPAs from the API (<c>Hosting:ServeSpas</c>, default off;
/// on the VM IIS serves them, so it stays off there). Files come from <c>{SpaRoot}/web</c> and
/// <c>{SpaRoot}/admin-console</c> (<c>Hosting:SpaRoot</c>, default <c>{ContentRoot}/wwwroot</c>).
/// Which app a request gets depends on <c>Tenancy:Mode</c>:
/// SingleHost picks by path (<c>/admin-console/**</c> = admin console, everything else = web);
/// Subdomain picks by host (<c>admin.</c>/<c>console.</c> = admin console, any other host = web).
/// </summary>
public static class SpaHostingExtensions
{
    public const string ServeSpasKey = "Hosting:ServeSpas";
    public const string SpaRootKey = "Hosting:SpaRoot";

    private const string WebFolder = "web";
    private const string AdminFolder = "admin-console";
    private static readonly PathString AdminConsolePath = "/admin-console";

    private static readonly PathString[] NonSpaPrefixes =
    {
        "/api", "/admin", "/hubs", "/health"
    };

    /// <summary>
    /// Serves the static files of the chosen SPA. Call after the security headers, HSTS and HTTPS
    /// redirection, but before tenant resolution, authentication, rate limiting and CSRF, so asset
    /// requests get the headers and then short-circuit. No-op unless <c>Hosting:ServeSpas</c> is true.
    /// </summary>
    public static WebApplication UseSpaHosting(this WebApplication app)
    {
        var settings = SpaHostingSettings.TryRead(app);
        if (settings is null)
            return app;

        var adminFiles = new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(settings.AdminRoot),
            RequestPath = settings.Mode == TenancyMode.SingleHost ? AdminConsolePath : PathString.Empty
        };
        var webFiles = new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(settings.WebRoot)
        };

        app.UseWhen(ctx => settings.IsAdminRequest(ctx), branch => branch.UseStaticFiles(adminFiles));
        app.UseWhen(ctx => !settings.IsAdminRequest(ctx), branch => branch.UseStaticFiles(webFiles));
        return app;
    }

    /// <summary>
    /// Maps one anonymous fallback that sends the chosen SPA's <c>index.html</c> for client-side
    /// deep links. API-ish paths and paths with a file extension keep their normal 404.
    /// No-op unless <c>Hosting:ServeSpas</c> is true.
    /// </summary>
    public static WebApplication MapSpaHostingFallbacks(this WebApplication app)
    {
        var settings = SpaHostingSettings.TryRead(app);
        if (settings is null)
            return app;

        var webIndex = Path.Combine(settings.WebRoot, "index.html");
        var adminIndex = Path.Combine(settings.AdminRoot, "index.html");

        app.MapFallback((HttpContext ctx) =>
        {
            if (!IsSpaFallbackRequest(ctx.Request))
                return Results.NotFound();

            var index = settings.IsAdminRequest(ctx) ? adminIndex : webIndex;
            if (!File.Exists(index))
                return Results.NotFound();

            ctx.Response.Headers.CacheControl = "no-cache";
            return Results.File(index, "text/html");
        }).AllowAnonymous();

        return app;
    }

    private static bool IsSpaFallbackRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            return false;

        return !IsApiPath(request.Path) && !Path.HasExtension(request.Path.Value);
    }

    /// <summary>
    /// True for paths that belong to the API (<c>/api</c>, <c>/admin</c>, <c>/hubs</c>,
    /// <c>/health</c>, segment-wise), never to an SPA. Shared with the security headers.
    /// </summary>
    internal static bool IsApiPath(PathString path)
    {
        foreach (var prefix in NonSpaPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private sealed class SpaHostingSettings
    {
        public required TenancyMode Mode { get; init; }
        public required string RootDomain { get; init; }
        public required string WebRoot { get; init; }
        public required string AdminRoot { get; init; }

        public static SpaHostingSettings? TryRead(WebApplication app)
        {
            var config = app.Configuration;
            if (!config.GetValue<bool>(ServeSpasKey))
                return null;

            var spaRoot = config[SpaRootKey];
            if (string.IsNullOrWhiteSpace(spaRoot))
                spaRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");

            var webRoot = Path.GetFullPath(Path.Combine(spaRoot, WebFolder));
            var adminRoot = Path.GetFullPath(Path.Combine(spaRoot, AdminFolder));
            foreach (var folder in new[] { webRoot, adminRoot })
            {
                if (!Directory.Exists(folder))
                    throw new InvalidOperationException(
                        $"{ServeSpasKey} is true but the SPA folder '{folder}' does not exist. " +
                        $"Deploy the build output there or set {SpaRootKey}.");
            }

            return new SpaHostingSettings
            {
                Mode = TenancyModeReader.Read(config),
                RootDomain = (config["Tenancy:RootDomain"] ?? string.Empty).Trim().ToLowerInvariant(),
                WebRoot = webRoot,
                AdminRoot = adminRoot
            };
        }

        public bool IsAdminRequest(HttpContext ctx)
        {
            if (Mode == TenancyMode.SingleHost)
                return ctx.Request.Path.StartsWithSegments(AdminConsolePath, StringComparison.OrdinalIgnoreCase);

            var host = ctx.Request.Host.Host.Trim().ToLowerInvariant();
            var suffix = "." + RootDomain;
            if (RootDomain.Length == 0 || !host.EndsWith(suffix, StringComparison.Ordinal))
                return false;

            var subdomain = host[..^suffix.Length];
            return HostTenantResolutionMiddleware.AdminSubdomains.Contains(subdomain);
        }
    }
}
