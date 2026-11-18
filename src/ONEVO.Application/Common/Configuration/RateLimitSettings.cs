namespace ONEVO.Application.Common.Configuration;

/// <summary>
/// Global API rate limits (task 07), bound from the <c>RateLimiting</c> section. Read them through
/// <see cref="ServiceInterfaces.IRateLimitSettingsProvider"/>, never from options directly, so a
/// database/admin-app provider can replace the configuration one. Every limit and its default is
/// documented in docs/deployment/rate-limits.md.
/// </summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Master switch. False lets every request through (the auth limiter still applies).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Coarse per-IP flood guard checked before authentication on /api/v1 and /admin/v1, so a
    /// flood of forged cookies or tokens is cut before any credential is validated. Every request
    /// from the IP counts, signed in or not, so keep it well above an office's combined traffic.
    /// </summary>
    public SlidingWindowLimit PreAuthentication { get; set; } = new() { PermitLimit = 6000, WindowSeconds = 60, SegmentsPerWindow = 6 };

    /// <summary>/api/v1 requests with no signed-in user or tray device, by client IP.</summary>
    public SlidingWindowLimit Anonymous { get; set; } = new() { PermitLimit = 300, WindowSeconds = 60, SegmentsPerWindow = 6 };

    /// <summary>/api/v1 requests from a signed-in tenant user, by user. 100 burst, 600 per minute.</summary>
    public TokenBucketLimit SignedIn { get; set; } = new() { TokenLimit = 100, TokensPerPeriod = 10, ReplenishmentPeriodSeconds = 1 };

    /// <summary>/api/v1 requests from a tray device token, by device. 60 burst, 300 per minute.</summary>
    public TokenBucketLimit TrayDevice { get; set; } = new() { TokenLimit = 60, TokensPerPeriod = 5, ReplenishmentPeriodSeconds = 1 };

    /// <summary>
    /// Safety ceiling shared by every user and tray device of one tenant, chained after the
    /// per-user or per-device limit. 3000 burst, 12000 per minute.
    /// </summary>
    public TokenBucketLimit TenantCeiling { get; set; } = new() { TokenLimit = 3000, TokensPerPeriod = 200, ReplenishmentPeriodSeconds = 1 };

    /// <summary>/admin/v1 requests with no signed-in platform admin, by client IP.</summary>
    public SlidingWindowLimit AdminAnonymous { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60, SegmentsPerWindow = 6 };

    /// <summary>/admin/v1 requests from a signed-in platform admin, by admin user. 60 burst, 300 per minute.</summary>
    public TokenBucketLimit AdminSignedIn { get; set; } = new() { TokenLimit = 60, TokensPerPeriod = 5, ReplenishmentPeriodSeconds = 1 };

    /// <summary>Returns the problems that make these settings unusable; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        PreAuthentication.Validate(nameof(PreAuthentication), errors);
        Anonymous.Validate(nameof(Anonymous), errors);
        SignedIn.Validate(nameof(SignedIn), errors);
        TrayDevice.Validate(nameof(TrayDevice), errors);
        TenantCeiling.Validate(nameof(TenantCeiling), errors);
        AdminAnonymous.Validate(nameof(AdminAnonymous), errors);
        AdminSignedIn.Validate(nameof(AdminSignedIn), errors);
        return errors;
    }
}

/// <summary>At most <see cref="PermitLimit"/> requests in any rolling window.</summary>
public sealed class SlidingWindowLimit
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public int SegmentsPerWindow { get; set; }

    internal void Validate(string name, List<string> errors)
    {
        if (PermitLimit < 1) errors.Add($"{name}.PermitLimit must be at least 1.");
        if (WindowSeconds < 1) errors.Add($"{name}.WindowSeconds must be at least 1.");
        if (SegmentsPerWindow < 1) errors.Add($"{name}.SegmentsPerWindow must be at least 1.");
    }
}

/// <summary>
/// Burst of up to <see cref="TokenLimit"/> requests, refilled by <see cref="TokensPerPeriod"/>
/// every <see cref="ReplenishmentPeriodSeconds"/>. Sustained rate per minute =
/// TokensPerPeriod * 60 / ReplenishmentPeriodSeconds.
/// </summary>
public sealed class TokenBucketLimit
{
    public int TokenLimit { get; set; }
    public int TokensPerPeriod { get; set; }
    public int ReplenishmentPeriodSeconds { get; set; }

    internal void Validate(string name, List<string> errors)
    {
        if (TokenLimit < 1) errors.Add($"{name}.TokenLimit must be at least 1.");
        if (TokensPerPeriod < 1) errors.Add($"{name}.TokensPerPeriod must be at least 1.");
        if (ReplenishmentPeriodSeconds < 1) errors.Add($"{name}.ReplenishmentPeriodSeconds must be at least 1.");
    }
}
