using System.Security.Claims;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.ServiceInterfaces;
using Serilog.AspNetCore;
using Serilog.Events;

namespace ONEVO.Api.Logging;

/// <summary>
/// One log line per HTTP request (Improvements task 06): route template rather than raw URL, so
/// timings group per endpoint; tenant, user and correlation id (from the log context); total
/// elapsed time; and the SQL command count and time from <see cref="IRequestSqlMetrics"/>.
/// Requests slower than <see cref="PerformanceOptions.SlowRequestThresholdMs"/> log as warnings.
/// </summary>
public static class RequestLogging
{
    public const string UnmatchedRoute = "(unmatched)";

    public const string MessageTemplate =
        "HTTP {RequestMethod} {RouteTemplate} responded {StatusCode} in {Elapsed:0.0} ms " +
        "({SqlCommandCount} SQL, {SqlElapsedMs:0.0} ms)";

    public static void Configure(RequestLoggingOptions options)
    {
        options.MessageTemplate = MessageTemplate;
        options.GetLevel = GetLevel;
        options.EnrichDiagnosticContext = (diag, ctx) => Enrich((name, value) => diag.Set(name, value), ctx);
    }

    public static void Enrich(Action<string, object> set, HttpContext ctx)
    {
        set("RouteTemplate", (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? UnmatchedRoute);
        set("TenantId", ResolveTenantId(ctx));
        set("UserId", ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anon");

        var sql = ctx.RequestServices?.GetService<IRequestSqlMetrics>();
        set("SqlCommandCount", sql?.CommandCount ?? 0);
        set("SqlElapsedMs", sql?.TotalElapsed.TotalMilliseconds ?? 0d);
    }

    public static LogEventLevel GetLevel(HttpContext ctx, double elapsedMs, Exception? ex)
    {
        if (ex is not null || ctx.Response.StatusCode > 499)
            return LogEventLevel.Error;

        var thresholdMs = ctx.RequestServices?.GetService<IOptions<PerformanceOptions>>()?.Value.SlowRequestThresholdMs
            ?? new PerformanceOptions().SlowRequestThresholdMs;

        return elapsedMs > thresholdMs ? LogEventLevel.Warning : LogEventLevel.Information;
    }

    private static string ResolveTenantId(HttpContext ctx)
    {
        var claim = ctx.User?.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrEmpty(claim))
            return claim;

        // Before sign-in (login, session exchange) the tenant comes from the host or header.
        var tenant = ctx.RequestServices?.GetService<ITenantContext>();
        return tenant is { IsResolved: true } ? tenant.TenantId.ToString() : "anon";
    }
}
