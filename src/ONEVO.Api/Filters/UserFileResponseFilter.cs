using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace ONEVO.Api.Filters;

/// <summary>
/// Task 23: every file the API returns (any <see cref="FileResult"/>) is a download, never a page.
/// Registered globally in Program.cs, so no controller has to remember it.
///
/// - Content-Disposition: attachment, unless the action already named a download file (MVC then
///   writes attachment itself), or the type is one of the raster images that uploads re-encode
///   (PNG, JPEG, WebP), which may be shown inline. A raster image with nosniff cannot run script, and
///   the sandbox CSP below stops it doing anything if opened directly.
/// - X-Content-Type-Options: nosniff, so the browser never guesses a more dangerous type.
/// - Content-Security-Policy: sandbox, so even a file opened in a tab gets a unique opaque origin.
/// - Cross-Origin-Resource-Policy: same-site instead of the global same-origin. The web app loads
///   avatars and logos with &lt;img src&gt; from the API; locally that is another port
///   (https://acme.crownus.test:4200 → :7229), which is another origin but the same site. Session
///   cookies are SameSite=Strict, so a cross-site page could never load these files signed in anyway.
/// </summary>
public sealed class UserFileResponseFilter : IAsyncResultFilter
{
    public const string FileCsp = "default-src 'none'; frame-ancestors 'none'; sandbox";

    private static readonly HashSet<string> InlineImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp"
    };

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is FileResult file)
        {
            var headers = context.HttpContext.Response.Headers;
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers[HeaderNames.ContentSecurityPolicy] = FileCsp;
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            // A named download already gets "attachment; filename=..." from MVC.
            if (string.IsNullOrEmpty(file.FileDownloadName))
            {
                headers[HeaderNames.ContentDisposition] = DispositionFor(file.ContentType);
            }
        }

        return next();
    }

    public static string DispositionFor(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return mediaType is not null && InlineImageTypes.Contains(mediaType) ? "inline" : "attachment";
    }
}
