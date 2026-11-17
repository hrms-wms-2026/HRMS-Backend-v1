using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Api.Middleware;

/// <summary>
/// Opens the request's database connection once, so EF does not open and close it around every query.
/// TenantRlsInterceptor keeps SESSION_CONTEXT right on the open connection; the DbContext closes it when the
/// request scope ends. Hubs and health checks are left as they are.
/// Design: docs/superpowers/specs/next/2026-11-17-one-connection-per-request-design.md.
/// </summary>
public sealed class RequestDbConnectionMiddleware
{
    private readonly RequestDelegate _next;

    public RequestDbConnectionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        if (AppliesTo(context.Request.Path))
            await db.Database.OpenConnectionAsync(context.RequestAborted);
        await _next(context);
    }

    public static bool AppliesTo(PathString path)
        => path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);
}
