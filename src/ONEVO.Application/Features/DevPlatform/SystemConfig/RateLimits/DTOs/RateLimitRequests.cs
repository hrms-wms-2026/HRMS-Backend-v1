namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;

/// <summary>
/// Body of PUT .../rate-limits/{limitName}. <see cref="Burst"/> is required for token-bucket limits
/// and must be absent for Anonymous and AdminAnonymous. <see cref="RowVersion"/> is the value from the
/// last GET (null when there was no override), so a second admin's stale save gets 409.
/// </summary>
public sealed record SetRateLimitRequest(int PerMinute, int? Burst, string? Reason, string? RowVersion);

/// <summary>Optional body of DELETE .../rate-limits/{limitName}; reason and rowVersion may also come from the query.</summary>
public sealed record RemoveRateLimitRequest(string? Reason, string? RowVersion);
