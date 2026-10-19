using System.Net;

namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public enum MeetingProviderFailureKind
{
    /// <summary>401: refresh the token once and retry once.</summary>
    Unauthorized,
    /// <summary>429: the provider rejected the request, so nothing was created; safe to retry after Retry-After.</summary>
    RateLimited,
    /// <summary>400/403/404 and other 4xx: retrying won't help.</summary>
    Permanent,
    /// <summary>Timeout, 5xx or connection failure: the meeting may or may not exist remotely.</summary>
    Ambiguous
}

/// <summary>A Teams/Zoom meeting API call failed; <see cref="Kind"/> drives what the caller may do next (spec §3.2.2).</summary>
public sealed class MeetingProviderException(
    MeetingProviderFailureKind kind, string message, TimeSpan? retryAfter = null, Exception? innerException = null, string? reason = null)
    : Exception(message, innerException)
{
    /// <summary>Zoom's error code 4711: the token lacks a scope the call needs (the user must reconnect).</summary>
    public const string MissingScopeReason = "missing_scope";

    public MeetingProviderFailureKind Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>A stable machine reason when the provider gave one (e.g. <see cref="MissingScopeReason"/>); otherwise null.</summary>
    public string? Reason { get; } = reason;

    public static MeetingProviderFailureKind Classify(HttpStatusCode status) => (int)status switch
    {
        401 => MeetingProviderFailureKind.Unauthorized,
        429 => MeetingProviderFailureKind.RateLimited,
        >= 500 => MeetingProviderFailureKind.Ambiguous,
        _ => MeetingProviderFailureKind.Permanent
    };
}
