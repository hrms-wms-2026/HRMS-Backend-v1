using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.RepositoryInterfaces;

/// <summary>A plan as the rate limit screens need it.</summary>
public sealed record RateLimitPlanRef(Guid Id, bool IsActive);

/// <summary>A tenant as the rate limit screens need it.</summary>
public sealed record RateLimitTenantRef(Guid Id, Guid? SubscriptionPlanId, bool IsArchived);

public sealed record RateLimitChangeFilter(string? Scope, Guid? TenantId, Guid? SubscriptionPlanId);

public interface IRateLimitSettingRepository
{
    /// <summary>Every global row, plus the rows of the given plan and tenant when set. No tracking.</summary>
    Task<IReadOnlyList<RateLimitOverrideRecord>> ListForScopeAsync(Guid? planId, Guid? tenantId, CancellationToken ct);

    /// <summary>The tracked row for one scope and limit, or null.</summary>
    Task<RateLimitSetting?> GetTrackedAsync(
        string scope, Guid? planId, Guid? tenantId, string limitName, CancellationToken ct);

    Task<RateLimitPlanRef?> GetPlanAsync(Guid planId, CancellationToken ct);

    Task<RateLimitTenantRef?> GetTenantAsync(Guid tenantId, CancellationToken ct);

    /// <summary>Names of the given tenants in one query; ids that do not exist are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetTenantNamesAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken ct);

    /// <summary>
    /// Sets the version the caller last read, so the save fails with
    /// <see cref="ONEVO.Application.Common.Exceptions.ConcurrencyConflictException"/> if the row changed.
    /// Returns false when the token is not a valid version.
    /// </summary>
    bool SetExpectedVersion(RateLimitSetting setting, string rowVersion);

    void Add(RateLimitSetting setting);

    void Remove(RateLimitSetting setting);

    void AddChange(RateLimitSettingChange change);

    /// <summary>
    /// Saves the setting and its history row in one transaction. Throws
    /// ConcurrencyConflictException on a stale version and UniqueConstraintConflictException when
    /// another admin created the same override first.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>History, newest first.</summary>
    Task<PagedResult<RateLimitChangeDto>> ListChangesAsync(
        RateLimitChangeFilter filter, int page, int pageSize, CancellationToken ct);
}
