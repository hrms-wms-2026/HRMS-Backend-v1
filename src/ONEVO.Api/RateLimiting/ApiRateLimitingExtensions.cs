using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;

namespace ONEVO.Api.RateLimiting;

/// <summary>
/// Global API rate limiter (task 07), built on the ASP.NET Core rate limiter. Partitions:
/// anonymous by IP (sliding window), signed-in tenant user by user (token bucket), tray device by
/// device (token bucket), then a chained per-tenant ceiling; /admin/v1 has its own anonymous-by-IP
/// and per-admin rules. Limits come from <see cref="IRateLimitSettingsProvider"/>. Counters are
/// process-local; task 21 moves them to a shared store. The strict auth endpoint limits stay in
/// AuthRateLimitingMiddleware.
/// </summary>
public static class ApiRateLimitingExtensions
{
    private const string LoggerCategory = "ONEVO.Api.RateLimiting";
    private const int FallbackRetryAfterSeconds = 60;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitSettings>().Bind(configuration.GetSection(RateLimitSettings.SectionName));
        services.TryAddSingleton<IRateLimitSettingsProvider, OptionsRateLimitSettingsProvider>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(SubjectPartition),
                PartitionedRateLimiter.Create<HttpContext, string>(TenantCeilingPartition));
            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    /// <summary>
    /// Call before UseAuthentication: a coarse per-IP guard, so floods of forged credentials are
    /// cut before any cookie or token is validated.
    /// </summary>
    public static IApplicationBuilder UseApiPreAuthenticationRateLimiting(this IApplicationBuilder app) =>
        app.UseMiddleware<PreAuthenticationRateLimitingMiddleware>();

    /// <summary>Call after UseAuthentication so signed-in requests are partitioned by identity.</summary>
    public static IApplicationBuilder UseApiRateLimiting(this IApplicationBuilder app)
    {
        // Resolve once at startup so invalid limits fail the boot instead of the first request.
        app.ApplicationServices.GetRequiredService<IRateLimitSettingsProvider>();

        app.UseMiddleware<RateLimitSubjectMiddleware>();
        return app.UseRateLimiter();
    }

    private static RateLimitPartition<string> SubjectPartition(HttpContext context)
    {
        var subject = RateLimitSubjectMiddleware.GetSubject(context);
        if (subject.Kind == RateLimitSubjectKind.Exempt)
            return RateLimitPartition.GetNoLimiter("exempt");

        var snapshot = SettingsFor(context, subject.TenantId);
        var settings = snapshot.Settings;
        if (!settings.Enabled)
            return RateLimitPartition.GetNoLimiter("disabled");

        var key = subject.PartitionKey(snapshot.Version);
        return subject.Kind switch
        {
            RateLimitSubjectKind.Anonymous => SlidingWindow(key, settings.Anonymous),
            RateLimitSubjectKind.User => TokenBucket(key, settings.SignedIn),
            RateLimitSubjectKind.TrayDevice => TokenBucket(key, settings.TrayDevice),
            RateLimitSubjectKind.AdminAnonymous => SlidingWindow(key, settings.AdminAnonymous),
            RateLimitSubjectKind.Admin => TokenBucket(key, settings.AdminSignedIn),
            _ => RateLimitPartition.GetNoLimiter("exempt"),
        };
    }

    private static RateLimitPartition<string> TenantCeilingPartition(HttpContext context)
    {
        var subject = RateLimitSubjectMiddleware.GetSubject(context);
        if (!subject.CountsAgainstTenant)
            return RateLimitPartition.GetNoLimiter("no-tenant");

        var tenantId = subject.TenantId!.Value;
        var snapshot = SettingsFor(context, tenantId);
        if (!snapshot.Settings.Enabled)
            return RateLimitPartition.GetNoLimiter("disabled");

        return TokenBucket(RateLimitSubject.ForTenantCeiling(tenantId, snapshot.Version), snapshot.Settings.TenantCeiling);
    }

    private static RateLimitSettingsSnapshot SettingsFor(HttpContext context, Guid? tenantId) =>
        context.RequestServices.GetRequiredService<IRateLimitSettingsProvider>().GetSettings(tenantId);

    private static RateLimitPartition<string> SlidingWindow(string key, SlidingWindowLimit limit) =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
            SegmentsPerWindow = limit.SegmentsPerWindow,
            QueueLimit = 0,
        });

    private static RateLimitPartition<string> TokenBucket(string key, TokenBucketLimit limit) =>
        RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = limit.TokenLimit,
            TokensPerPeriod = limit.TokensPerPeriod,
            ReplenishmentPeriod = TimeSpan.FromSeconds(limit.ReplenishmentPeriodSeconds),
            QueueLimit = 0,
        });

    /// <summary>Same 429 response as the hand-written limiter it replaces.</summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var subject = RateLimitSubjectMiddleware.GetSubject(rejected.HttpContext);
        await WriteTooManyRequestsAsync(
            rejected.HttpContext, rejected.Lease, subject.Kind.ToString(), subject.TenantId, cancellationToken);
    }

    internal static async Task WriteTooManyRequestsAsync(
        HttpContext context, RateLimitLease lease, string partition, Guid? tenantId, CancellationToken cancellationToken)
    {
        var retryAfterSeconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : FallbackRetryAfterSeconds;

        ONEVO.Application.Common.Observability.CrownusMetrics.RecordRateLimitRejection("api", partition);
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(LoggerCategory)
            .LogWarning(
                "API rate limit exceeded. Partition={Partition}, TenantId={TenantId}, Path={Path}, RetryAfterSeconds={RetryAfterSeconds}",
                partition,
                tenantId,
                context.Request.Path.Value,
                retryAfterSeconds);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(new
        {
            type = ONEVO.Api.Errors.ProblemTypes.For("rate-limit"),
            title = "Too Many Requests",
            status = 429,
            detail = "Too many requests. Please try again later.",
        }, cancellationToken);
    }
}
