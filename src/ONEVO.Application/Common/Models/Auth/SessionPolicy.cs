namespace ONEVO.Application.Common.Models.Auth;

/// <summary>Single session cookie with sliding expiry (no refresh token) — see fixed architecture decision.</summary>
public static class SessionPolicy
{
    public static readonly TimeSpan SlidingWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RenewalThreshold = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);
}
