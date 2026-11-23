using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ONEVO.Application.Common.Observability;

/// <summary>
/// Task 17: the API's own metrics, under one meter. OpenTelemetry exports them when an exporter is configured
/// (see docs/deployment/observability.md); without one, recording costs almost nothing. Tags never carry personal
/// data: job names, outcomes, limiter names, status codes.
/// </summary>
public static class CrownusMetrics
{
    public const string MeterName = "Crownus.Api";

    /// <summary>Activity source for the API's own spans and tags.</summary>
    public const string ActivitySourceName = "Crownus.Api";

    public static readonly Meter Meter = new(MeterName);
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>One per background job run: tags <c>job</c>, <c>outcome</c> (completed, failed, timed_out, skipped).</summary>
    public static readonly Counter<long> JobRuns = Meter.CreateCounter<long>(
        "crownus.jobs.runs", unit: "{run}", description: "Background job runs by outcome.");

    /// <summary>Duration of runs that did work (not skipped): tags <c>job</c>, <c>outcome</c>.</summary>
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "crownus.jobs.duration", unit: "ms", description: "Background job run duration.");

    /// <summary>Requests answered 429: tags <c>limiter</c> (api, auth) and <c>partition</c>.</summary>
    public static readonly Counter<long> RateLimitRejections = Meter.CreateCounter<long>(
        "crownus.ratelimit.rejections", unit: "{request}", description: "Requests rejected by a rate limiter.");

    /// <summary>Failed sign-ins: tags <c>surface</c> (tenant, admin) and <c>status</c> (HTTP status returned).</summary>
    public static readonly Counter<long> SignInFailures = Meter.CreateCounter<long>(
        "crownus.auth.signin_failures", unit: "{attempt}", description: "Failed sign-in attempts.");

    public static void RecordSignInFailure(string surface, int status)
        => SignInFailures.Add(1, new TagList { { "surface", surface }, { "status", status } });

    public static void RecordRateLimitRejection(string limiter, string partition)
        => RateLimitRejections.Add(1, new TagList { { "limiter", limiter }, { "partition", partition } });
}
