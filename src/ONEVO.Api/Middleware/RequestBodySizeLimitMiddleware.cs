using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;

namespace ONEVO.Api.Middleware;

/// <summary>
/// Task 23: a small global request body limit. Every endpoint accepts at most
/// <see cref="DefaultMaxBodyBytes"/> unless it declares its own limit with
/// <c>[RequestSizeLimit]</c> (upload actions, see docs/reports/security/2026-11-18-task-23-headers-uploads-limits.md).
///
/// Runs right after UseRouting, so the matched endpoint's limit is known and an oversized body is
/// refused before tenant resolution, authentication or anything else reads it:
/// - a declared Content-Length over the limit gets 413 at once;
/// - for chunked bodies, Kestrel's per-request limit is set to the same value, so reading past it
///   throws BadHttpRequestException(413), which ExceptionHandlerMiddleware turns into a 413.
/// Kestrel's server-wide MaxRequestBodySize is also set to the default in Program.cs, as a backstop
/// for anything that reads the body before this middleware.
/// </summary>
public sealed class RequestBodySizeLimitMiddleware
{
    public const long DefaultMaxBodyBytes = 1024 * 1024;

    private readonly RequestDelegate _next;

    public RequestBodySizeLimitMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var limit = ResolveLimit(context.GetEndpoint());

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = limit;

        if (limit is { } max && context.Request.ContentLength is { } length && length > max)
        {
            await WriteTooLargeAsync(context, max);
            return;
        }

        await _next(context);
    }

    /// <summary>The endpoint's own [RequestSizeLimit]/[DisableRequestSizeLimit] wins; otherwise the default.</summary>
    public static long? ResolveLimit(Endpoint? endpoint)
    {
        var metadata = endpoint?.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
        return metadata is null ? DefaultMaxBodyBytes : metadata.MaxRequestBodySize;
    }

    internal static Task WriteTooLargeAsync(HttpContext context, long max)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/problem+json";
        var correlationId = context.Items["X-Correlation-Id"]?.ToString() ?? Guid.NewGuid().ToString();
        var body = JsonSerializer.Serialize(new
        {
            type = ONEVO.Api.Errors.ProblemTypes.For("payload-too-large"),
            title = "Payload Too Large",
            status = StatusCodes.Status413PayloadTooLarge,
            detail = $"The request body is larger than the {max} bytes this endpoint accepts.",
            instance = context.Request.Path.Value,
            correlationId
        });
        return context.Response.WriteAsync(body);
    }
}
