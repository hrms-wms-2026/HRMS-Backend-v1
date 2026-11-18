namespace ONEVO.Application.Common.Models.Auth;

/// <summary>
/// Platform admin session lifetime (security audit 2026-11-05, finding F-03). An admin session can read every
/// tenant, so unlike the tenant <see cref="SessionPolicy"/> it ends after 30 minutes without activity and,
/// however active, 12 hours after sign-in.
/// </summary>
public static class AdminSessionPolicy
{
    /// <summary>Sliding window: each renewal sets the expiry to now + this, never more.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Hard cap from session creation; renewals never pass it.</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);
}
