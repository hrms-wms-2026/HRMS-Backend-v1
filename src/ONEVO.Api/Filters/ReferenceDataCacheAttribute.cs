using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ONEVO.Api.Filters;

/// <summary>
/// Improvements task 10: reference data that rarely changes (leave types, work modes, project
/// categories, the permission catalogue) gets an ETag from its JSON body and
/// <c>Cache-Control: private, no-cache</c>. The browser keeps a private copy but asks again every
/// time; when nothing changed the API answers 304 with no body. Asking every time means a copy is
/// never shown to a user whose session or permissions no longer allow it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ReferenceDataCacheAttribute : Attribute, IAsyncResultFilter
{
    internal const string CacheControl = "private, no-cache";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method)
            || context.Result is not ObjectResult { Value: { } value } result
            || (result.StatusCode ?? StatusCodes.Status200OK) != StatusCodes.Status200OK)
        {
            await next();
            return;
        }

        var json = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var body = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), json);
        var etag = $"W/\"{Convert.ToHexString(SHA256.HashData(body), 0, 16)}\"";

        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = CacheControl;

        if (Matches(http.Request.Headers.IfNoneMatch, etag))
            context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);

        await next();
    }

    private static bool Matches(Microsoft.Extensions.Primitives.StringValues ifNoneMatch, string etag)
    {
        foreach (var header in ifNoneMatch)
        {
            foreach (var candidate in (header ?? string.Empty).Split(',', StringSplitOptions.TrimEntries))
            {
                // Weak comparison (RFC 9110 13.1.2): W/"x" and "x" match.
                if (candidate == "*" || StripWeak(candidate) == StripWeak(etag))
                    return true;
            }
        }

        return false;
    }

    private static string StripWeak(string tag) => tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag;
}
