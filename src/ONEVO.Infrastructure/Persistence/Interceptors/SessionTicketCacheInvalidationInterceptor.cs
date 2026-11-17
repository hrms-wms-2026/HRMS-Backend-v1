using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Identity.Sessions;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Evicts cached authentication tickets (improvement task 03) when a tracked write changes anything
/// a ticket is built from: the session row, the user, role assignments, role permissions,
/// permission overrides, the employee (legal entity), the tenant and its subscription, and the
/// platform-admin equivalents.
/// <para>
/// Evictions run when the change is saved and again when the surrounding transaction commits, so a
/// request that rebuilt a ticket between the two can never keep stale data. Bulk ExecuteUpdate /
/// ExecuteDelete writes bypass the change tracker; the repositories that issue them on these
/// tables evict explicitly.
/// </para>
/// </summary>
public sealed class SessionTicketCacheInvalidationInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private static readonly HashSet<string> SessionRenewalOnlyProperties =
        [nameof(Session.ExpiresAt), nameof(Session.LastActivityAt)];

    private readonly ISessionTicketCache _cache;
    private readonly ConditionalWeakTable<DbContext, List<Action<ISessionTicketCache>>> _pending = new();

    public SessionTicketCacheInvalidationInterceptor(ISessionTicketCache cache)
    {
        _cache = cache;
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

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Flush(eventData.Context);

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Flush(eventData.Context);

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

        var actions = new List<Action<ISessionTicketCache>>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                AddEvictions(entry, actions);
        }

        if (actions.Count == 0)
            return;

        // Evict now as well: the grace window then also covers the time until commit.
        foreach (var action in actions)
            action(_cache);

        var pending = _pending.GetOrCreateValue(context);
        lock (pending)
            pending.AddRange(actions);
    }

    private void AfterSave(DbContext? context)
    {
        // Inside an explicit transaction the rows are not visible yet; evict again on commit.
        if (context is not null && context.Database.CurrentTransaction is null)
            Flush(context);
    }

    private void Flush(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending))
            return;

        Action<ISessionTicketCache>[] actions;
        lock (pending)
        {
            actions = [.. pending];
            pending.Clear();
        }

        foreach (var action in actions)
            action(_cache);
    }

    private static void AddEvictions(EntityEntry entry, List<Action<ISessionTicketCache>> actions)
    {
        var modified = entry.State == EntityState.Modified;
        switch (entry.Entity)
        {
            case Session session when entry.State != EntityState.Added:
            {
                var keyHash = Original(entry, nameof(Session.KeyHash), session.KeyHash);
                if (modified && OnlyRenewalPropertiesChanged(entry))
                {
                    // The ticket store's own renewal write: drop the entry so the new expiry is read.
                    actions.Add(c => Drop(c, keyHash));
                    break;
                }
                actions.Add(c => c.EvictSession(keyHash));
                break;
            }
            case PlatformUserSession platformSession when entry.State != EntityState.Added:
            {
                var tokenHash = Original(entry, nameof(PlatformUserSession.TokenHash), platformSession.TokenHash);
                if (modified && OnlyRenewalPropertiesChanged(entry))
                {
                    actions.Add(c => Drop(c, tokenHash));
                    break;
                }
                actions.Add(c => c.EvictSession(tokenHash));
                break;
            }
            case User user when entry.State != EntityState.Added:
                actions.Add(c => c.EvictUser(user.Id));
                break;
            case UserRole userRole:
                actions.Add(c => c.EvictUser(userRole.UserId));
                break;
            case UserPermissionOverride permissionOverride:
                actions.Add(c => c.EvictUser(permissionOverride.UserId));
                break;
            case Employee employee when entry.State != EntityState.Added:
            {
                var userId = employee.UserId;
                var originalUserId = Original(entry, nameof(Employee.UserId), userId);
                actions.Add(c => c.EvictUser(userId));
                if (originalUserId != userId)
                    actions.Add(c => c.EvictUser(originalUserId));
                break;
            }
            case Role role:
                actions.Add(c => c.EvictTenant(role.TenantId));
                break;
            case RolePermission rolePermission:
                actions.Add(c => c.EvictTenant(rolePermission.TenantId));
                break;
            case Tenant tenant when entry.State != EntityState.Added:
                actions.Add(c => c.EvictTenant(tenant.Id));
                break;
            case TenantSubscription subscription:
                actions.Add(c => c.EvictTenant(subscription.TenantId));
                break;
            case Permission when entry.State != EntityState.Added:
                actions.Add(c => c.EvictAll());
                break;
            case PlatformUser platformUser when entry.State != EntityState.Added:
                actions.Add(c => c.EvictUser(platformUser.Id));
                break;
            case PlatformUserRole platformUserRole:
                actions.Add(c => c.EvictUser(platformUserRole.UserId));
                break;
            case PlatformRole:
            case PlatformRolePermission:
                actions.Add(c => c.EvictTenant(SessionTicketCacheScopes.Platform));
                break;
        }
    }

    private static void Drop(ISessionTicketCache cache, string keyHash)
    {
        if (cache is ISessionTicketStoreCache storeCache)
            storeCache.DropSession(keyHash);
        else
            cache.EvictSession(keyHash);
    }

    private static bool OnlyRenewalPropertiesChanged(EntityEntry entry) =>
        entry.Properties.Where(p => p.IsModified).All(p => SessionRenewalOnlyProperties.Contains(p.Metadata.Name));

    private static T Original<T>(EntityEntry entry, string propertyName, T fallback)
    {
        var property = entry.Property(propertyName);
        return property.OriginalValue is T original ? original : fallback;
    }
}
