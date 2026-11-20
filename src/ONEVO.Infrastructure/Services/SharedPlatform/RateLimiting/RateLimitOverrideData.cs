namespace ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;

/// <summary>One row of <c>rate_limit_settings</c>.</summary>
public sealed record RateLimitOverrideRow(
    string Scope, Guid? SubscriptionPlanId, Guid? TenantId, string LimitName, int PerMinute, int? Burst);

/// <summary>Everything a rebuild needs: every override row and each tenant's plan.</summary>
public sealed record RateLimitOverrideData(
    IReadOnlyList<RateLimitOverrideRow> Rows, IReadOnlyDictionary<Guid, Guid> TenantPlans)
{
    public static RateLimitOverrideData Empty { get; } = new([], new Dictionary<Guid, Guid>());
}

/// <summary>
/// A cheap summary of the data, compared every 30 seconds to decide whether to reload:
/// row count and last update of <c>rate_limit_settings</c>, and a hash of the tenant-to-plan map.
/// </summary>
public sealed record RateLimitDataStamp(int RowCount, DateTimeOffset? LastUpdatedAt, int TenantPlansHash);

/// <summary>Reads the overrides. Swapped for a fake in unit tests.</summary>
public interface IRateLimitOverrideLoader
{
    Task<RateLimitOverrideData> LoadAsync(CancellationToken ct);

    Task<RateLimitDataStamp> GetStampAsync(CancellationToken ct);
}
