using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Common;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps <see cref="BaseEntity.CreatedAt"/>, <see cref="BaseEntity.CreatedById"/> and
/// <see cref="BaseEntity.UpdatedAt"/>.
///
/// <c>created_by_id</c> has a foreign key to <c>users</c> (task 39c), so it holds the signed-in tenant
/// user or <c>NULL</c>: <c>NULL</c> for seeders, jobs and anonymous paths, and for a platform admin,
/// whose id is a <c>platform_users</c> id. <see cref="ICurrentUser.UserId"/> reads the name identifier
/// of any scheme, so a platform admin is recognised by <see cref="ICurrentPlatformUserContext"/>, which
/// only trusts the admin scheme.
/// </summary>
public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentPlatformUserContext? _platformUser;

    public AuditableEntityInterceptor(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        ICurrentPlatformUserContext? platformUser = null)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _platformUser = platformUser;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context is null) return;

        var now = _dateTimeProvider.UtcNow;
        var userId = TenantUserIdOrNull();

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedById = userId;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private Guid? TenantUserIdOrNull()
    {
        if (!_currentUser.IsAuthenticated) return null;

        var userId = _currentUser.UserId;
        if (userId == Guid.Empty) return null;

        // The same name-identifier claim, but issued by the admin scheme: a platform_users id.
        if (_platformUser?.UserId == userId) return null;

        return userId;
    }
}
