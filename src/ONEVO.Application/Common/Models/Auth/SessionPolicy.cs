namespace ONEVO.Application.Common.Models.Auth;

/// <summary>
/// Single session cookie with sliding expiry (no refresh token) — see fixed architecture decision.
/// A 30-day sliding window with no meaningful absolute cap (AbsoluteLifetime is set far longer
/// than any real session would live) so day-to-day use — including gaps like a lunch break, a
/// weekend, or a period of leave — never forces a re-login, matching how Jira/ClickUp-style
/// tools behave. Sign-out and explicit revocation remain the way to end a session early.
/// </summary>
public static class SessionPolicy
{
    public static readonly TimeSpan SlidingWindow = TimeSpan.FromDays(30);
    public static readonly TimeSpan RenewalThreshold = TimeSpan.FromMinutes(15);

    /// <summary>Effectively unbounded — see summary above. Kept as a real (non-MaxValue) TimeSpan
    /// so DateTimeOffset arithmetic in the ticket stores can never overflow.</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(3650);
}
