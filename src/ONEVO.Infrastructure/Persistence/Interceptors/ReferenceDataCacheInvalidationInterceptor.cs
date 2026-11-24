using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Application.Common.Caching;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Drops cached reference data (improvements task 22, DEC-104) when a tracked write changes it, whichever handler made
/// the write. Same timing as the task 03 ticket interceptor: the entries are removed when the change is saved and again
/// when the surrounding transaction commits or rolls back, so a read that cached the old (or uncommitted) rows in between
/// cannot keep them. Removals go through <see cref="ICacheService"/>, which tells every other instance (task 21).
/// <para>
/// Bulk ExecuteUpdate / ExecuteDelete writes bypass the change tracker; none touches these tables today. A repository
/// that adds one must call <see cref="ITenantCache.InvalidateAsync"/> itself.
/// </para>
/// </summary>
public sealed class ReferenceDataCacheInvalidationInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    /// <summary>Which cached lookups each entity feeds. Tenant-owned entities drop their own tenant's entries.</summary>
    private static readonly Dictionary<Type, string[]> TenantLookups = new()
    {
        [typeof(TenantSubscription)] = [ReferenceDataCacheKeys.ActiveModules, ReferenceDataCacheKeys.AssignablePermissions],
        [typeof(LeaveType)] = [ReferenceDataCacheKeys.LeaveTypes],
        [typeof(WorkMode)] = [ReferenceDataCacheKeys.WorkModes],
        [typeof(ClockInPolicy)] = [ReferenceDataCacheKeys.ClockInPolicies],
        [typeof(ClockInLateDeductionRule)] = [ReferenceDataCacheKeys.ClockInPolicies],
        [typeof(HolidayCalendarSettings)] = [ReferenceDataCacheKeys.HolidayCalendarSettings],
        [typeof(TaskCategory)] = [ReferenceDataCacheKeys.TaskCategories],
        [typeof(ProjectCategory)] = [ReferenceDataCacheKeys.ProjectCategories],
    };

    /// <summary>Platform-wide entities: drop the lookup for every tenant.</summary>
    private static readonly Dictionary<Type, string[]> GlobalLookups = new()
    {
        [typeof(Permission)] = [ReferenceDataCacheKeys.UniversalPermissions, ReferenceDataCacheKeys.AssignablePermissions],
        [typeof(ModuleCatalogItem)] = [ReferenceDataCacheKeys.ActiveModuleCatalog],
    };

    private readonly ICacheService _cache;
    private readonly ConditionalWeakTable<DbContext, HashSet<string>> _pending = new();

    public ReferenceDataCacheInvalidationInterceptor(ICacheService cache)
    {
        _cache = cache;
    }

    /// <summary>The key prefixes a change to <paramref name="entity"/> removes (empty when it feeds no cached lookup).</summary>
    public static IEnumerable<string> PrefixesFor(object entity)
    {
        var type = entity.GetType();
        if (TenantLookups.TryGetValue(type, out var tenantNames) && entity is ITenantOwnedEntity owned)
        {
            foreach (var name in tenantNames)
                yield return ReferenceDataCacheKeys.TenantPrefix(owned.TenantId, name);
        }

        if (GlobalLookups.TryGetValue(type, out var globalNames))
        {
            foreach (var name in globalNames)
                yield return ReferenceDataCacheKeys.NamePrefix(name);
        }
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AfterSave(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        AfterSave(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => AfterSave(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        AfterSave(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Flush(eventData.Context);

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Flush(eventData.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
            return;

        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                prefixes.UnionWith(PrefixesFor(entry.Entity));
        }

        if (prefixes.Count == 0)
            return;

        // Remove now as well, so the time until commit is covered too.
        Remove(prefixes);

        var pending = _pending.GetOrCreateValue(context);
        lock (pending)
            pending.UnionWith(prefixes);
    }

    private void AfterSave(DbContext? context)
    {
        // Inside an explicit transaction the rows are not visible to other requests yet; remove again on commit.
        if (context is not null && context.Database.CurrentTransaction is null)
            Flush(context);
    }

    private void Flush(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending))
            return;

        string[] prefixes;
        lock (pending)
        {
            prefixes = [.. pending];
            pending.Clear();
        }

        Remove(prefixes);
    }

    private void Remove(IEnumerable<string> prefixes)
    {
        // MemoryCacheService completes synchronously; publishing to other instances never throws.
        foreach (var prefix in prefixes)
            _cache.RemoveByPrefixAsync(prefix).GetAwaiter().GetResult();
    }
}
