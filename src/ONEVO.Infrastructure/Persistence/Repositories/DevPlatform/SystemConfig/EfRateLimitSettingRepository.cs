using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

/// <summary>
/// Rate limit overrides and their history (Improvements task 47). Admin requests run in admin mode,
/// which the RLS policy (TenantOrSystem) allows to read and write both tables.
/// </summary>
public sealed class EfRateLimitSettingRepository : IRateLimitSettingRepository
{
    private const string RowVersionProperty = "RowVersion";
    private readonly ApplicationDbContext _db;

    public EfRateLimitSettingRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<RateLimitOverrideRecord>> ListForScopeAsync(
        Guid? planId, Guid? tenantId, CancellationToken ct)
    {
        var rows = await _db.RateLimitSettings.AsNoTracking()
            .Where(r => r.Scope == RateLimitSettingRules.GlobalScope
                || (planId != null && r.Scope == RateLimitSettingRules.PlanScope && r.SubscriptionPlanId == planId)
                || (tenantId != null && r.Scope == RateLimitSettingRules.TenantScope && r.TenantId == tenantId))
            .Select(r => new
            {
                r.Id, r.Scope, r.SubscriptionPlanId, r.TenantId, r.LimitName, r.PerMinute, r.Burst,
                r.UpdatedAt, r.UpdatedByPlatformUserId,
                Version = EF.Property<ulong>(r, RowVersionProperty),
            })
            .ToListAsync(ct);

        return rows.Select(r => new RateLimitOverrideRecord(
                r.Id, r.Scope, r.SubscriptionPlanId, r.TenantId, r.LimitName, r.PerMinute, r.Burst,
                r.UpdatedAt, r.UpdatedByPlatformUserId, RowVersionToken.ToToken(r.Version)))
            .ToList();
    }

    public Task<RateLimitSetting?> GetTrackedAsync(
        string scope, Guid? planId, Guid? tenantId, string limitName, CancellationToken ct) =>
        _db.RateLimitSettings.FirstOrDefaultAsync(r => r.Scope == scope
            && r.LimitName == limitName
            && r.SubscriptionPlanId == planId
            && r.TenantId == tenantId, ct);

    public Task<RateLimitPlanRef?> GetPlanAsync(Guid planId, CancellationToken ct) =>
        _db.SubscriptionPlans.AsNoTracking()
            .Where(p => p.Id == planId)
            .Select(p => new RateLimitPlanRef(p.Id, p.IsActive))
            .FirstOrDefaultAsync(ct);

    public Task<RateLimitTenantRef?> GetTenantAsync(Guid tenantId, CancellationToken ct) =>
        _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new RateLimitTenantRef(t.Id, t.SubscriptionPlanId, t.Status == TenantStatus.Cancelled))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetTenantNamesAsync(
        IReadOnlyCollection<Guid> tenantIds, CancellationToken ct)
    {
        if (tenantIds.Count == 0)
            return new Dictionary<Guid, string>();

        return await _db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }

    public bool SetExpectedVersion(RateLimitSetting setting, string rowVersion)
    {
        if (!RowVersionToken.TryParse(rowVersion, out var version))
            return false;

        _db.Entry(setting).Property(RowVersionProperty).OriginalValue = version;
        return true;
    }

    public void Add(RateLimitSetting setting) => _db.RateLimitSettings.Add(setting);

    public void Remove(RateLimitSetting setting) => _db.RateLimitSettings.Remove(setting);

    public void AddChange(RateLimitSettingChange change) => _db.RateLimitSettingChanges.Add(change);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            // One SaveChanges: the override and its history row commit together or not at all.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            throw new UniqueConstraintConflictException(ex);
        }
    }

    public async Task<PagedResult<RateLimitChangeDto>> ListChangesAsync(
        RateLimitChangeFilter filter, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.RateLimitSettingChanges.AsNoTracking();
        if (filter.Scope is not null)
            query = query.Where(c => c.Scope == filter.Scope);
        if (filter.TenantId is not null)
            query = query.Where(c => c.TenantId == filter.TenantId);
        if (filter.SubscriptionPlanId is not null)
            query = query.Where(c => c.SubscriptionPlanId == filter.SubscriptionPlanId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.ChangedAt)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new RateLimitChangeDto(
                c.Id,
                c.ChangedAt,
                c.PlatformUserId,
                _db.PlatformUsers.Where(u => u.Id == c.PlatformUserId).Select(u => u.FullName).FirstOrDefault(),
                c.Scope,
                c.SubscriptionPlanId,
                c.TenantId,
                c.LimitName,
                c.OldPerMinute,
                c.OldBurst,
                c.NewPerMinute,
                c.NewBurst,
                c.Reason,
                c.CorrelationId))
            .ToListAsync(ct);

        return new PagedResult<RateLimitChangeDto>(items, page, pageSize, total);
    }
}
