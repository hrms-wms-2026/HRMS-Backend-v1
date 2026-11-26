using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Api.Middleware;

/// <summary>
/// Opens the request's database connection once, so EF does not open and close it around every query.
/// TenantRlsInterceptor keeps SESSION_CONTEXT right on the open connection; the DbContext closes it when the
/// request scope ends. Runs after authentication, so rejected requests never take a connection, and skips
/// uploads, which would hold one while the body arrives. If the open fails (a failover), the request goes on
/// and EF opens per query with its retry, as it did before.
/// Design: docs/superpowers/specs/next/2026-11-17-one-connection-per-request-design.md.
/// </summary>
public sealed class RequestDbConnectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestDbConnectionMiddleware> _logger;

    public RequestDbConnectionMiddleware(RequestDelegate next, ILogger<RequestDbConnectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        if (AppliesTo(context.Request))
        {
            try
            {
                await db.Database.OpenConnectionAsync(context.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not open the request's database connection; EF will open it per query.");
            }
        }
        await _next(context);
    }

    public static bool AppliesTo(HttpRequest request)
        => (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase))
            && !IsUpload(request.ContentType);

    private static bool IsUpload(string? contentType)
        => contentType is not null
            && (contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)
                || contentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase));
}
