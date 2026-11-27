using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;

/// <summary>
/// Reads the overrides in system mode from a short-lived scope, as the outbox processor does.
/// The provider is a singleton, so it never holds a DbContext.
/// </summary>
public sealed class EfRateLimitOverrideLoader(IServiceScopeFactory scopes) : IRateLimitOverrideLoader
{
    public async Task<RateLimitOverrideData> LoadAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = SystemModeContext(scope);

        var rows = await db.RateLimitSettings.AsNoTracking()
            .Select(r => new RateLimitOverrideRow(r.Scope, r.SubscriptionPlanId, r.TenantId, r.LimitName, r.PerMinute, r.Burst))
            .ToListAsync(ct);
        var plans = await TenantPlansAsync(db, ct);
        return new RateLimitOverrideData(rows, plans);
    }

    public async Task<RateLimitDataStamp> GetStampAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = SystemModeContext(scope);

        var summary = await db.RateLimitSettings.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Last = g.Max(r => (DateTimeOffset?)r.UpdatedAt) })
            .FirstOrDefaultAsync(ct);
        var plans = await TenantPlansAsync(db, ct);
        return new RateLimitDataStamp(summary?.Count ?? 0, summary?.Last, HashPlans(plans));
    }

    internal static int HashPlans(IReadOnlyDictionary<Guid, Guid> plans)
    {
        var hash = new HashCode();
        foreach (var (tenant, plan) in plans.OrderBy(p => p.Key))
        {
            hash.Add(tenant);
            hash.Add(plan);
        }

        return hash.ToHashCode();
    }

    private static ApplicationDbContext SystemModeContext(AsyncServiceScope scope)
    {
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetSystemMode();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    private static async Task<IReadOnlyDictionary<Guid, Guid>> TenantPlansAsync(ApplicationDbContext db, CancellationToken ct) =>
        // Two columns only: the map is read on every 30-second check.
        await db.Tenants.AsNoTracking()
            .Where(t => t.SubscriptionPlanId != null)
            .Select(t => new { t.Id, PlanId = t.SubscriptionPlanId!.Value })
            .ToDictionaryAsync(t => t.Id, t => t.PlanId, ct);
}
