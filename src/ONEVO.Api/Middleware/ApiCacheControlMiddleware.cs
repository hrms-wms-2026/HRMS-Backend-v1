using ONEVO.Api.Extensions;

namespace ONEVO.Api.Middleware;

/// <summary>
/// Improvements task 10: API responses hold personal data, so by default no browser or proxy may
/// store them (<c>Cache-Control: no-store</c>). An endpoint that sets its own Cache-Control keeps
/// it: file downloads, and reference data through <see cref="Filters.ReferenceDataCacheAttribute"/>.
/// SPA files are not touched.
/// </summary>
public sealed class ApiCacheControlMiddleware(RequestDelegate next)
{
    internal const string Default = "no-store";

    public Task InvokeAsync(HttpContext context)
    {
        if (SpaHostingExtensions.IsApiPath(context.Request.Path))
        {
            context.Response.OnStarting(static state =>
            {
                var response = (HttpResponse)state;
                if (string.IsNullOrEmpty(response.Headers.CacheControl))
                    response.Headers.CacheControl = Default;
                return Task.CompletedTask;
            }, context.Response);
        }

        return next(context);
    }
}
