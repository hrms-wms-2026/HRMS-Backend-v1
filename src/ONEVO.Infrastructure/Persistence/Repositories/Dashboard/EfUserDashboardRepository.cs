using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Dashboard;

public class EfUserDashboardRepository : IUserDashboardRepository
{
    private readonly ApplicationDbContext _db;

    public EfUserDashboardRepository(ApplicationDbContext db) => _db = db;

    public Task<UserDashboard?> GetAsync(Guid tenantId, Guid userId, string dashboardKey, CancellationToken ct = default)
        => WithChildren().FirstOrDefaultAsync(
            d => d.TenantId == tenantId && d.UserId == userId && d.DashboardKey == dashboardKey, ct);

    public Task<UserDashboard?> GetByIdAsync(Guid tenantId, Guid dashboardId, CancellationToken ct = default)
        => WithChildren().FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == dashboardId, ct);

    public Task<UserDashboard?> GetByWidgetIdAsync(Guid tenantId, Guid widgetId, CancellationToken ct = default)
        => WithChildren().FirstOrDefaultAsync(
            d => d.TenantId == tenantId && d.Widgets.Any(w => w.Id == widgetId), ct);

    public async Task<IReadOnlyList<UserDashboard>> ListForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => await _db.UserDashboards.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.UserId == userId)
            .ToListAsync(ct);

    public async Task<bool> TryAddAsync(UserDashboard dashboard, CancellationToken ct = default)
    {
        AssignMissingIds(dashboard);
        _db.UserDashboards.Add(dashboard);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost the seeding race on (tenant, user, dashboard_key): the winner's row exists.
            _db.Entry(dashboard).State = EntityState.Detached;
            foreach (var widget in dashboard.Widgets)
            {
                foreach (var layout in widget.Layouts)
                    _db.Entry(layout).State = EntityState.Detached;
                _db.Entry(widget).State = EntityState.Detached;
            }
            return false;
        }
    }

    public async Task<bool> SaveWithVersionAsync(UserDashboard dashboard, long expectedVersion, CancellationToken ct = default)
    {
        AssignMissingIds(dashboard);
        // Widgets/layouts added to the graph carry pre-assigned keys (ValueGeneratedNever), so change detection
        // would treat them as existing rows; mark the untracked ones as Added explicitly.
        foreach (var widget in dashboard.Widgets)
        {
            if (_db.Entry(widget).State == EntityState.Detached)
                _db.Add(widget);
            foreach (var layout in widget.Layouts)
            {
                if (_db.Entry(layout).State == EntityState.Detached)
                    _db.Add(layout);
            }
        }

        // Removed widgets/layouts (dropped from the collections) become orphan deletes, soft-deleted by the interceptor.
        _db.ChangeTracker.DetectChanges();

        var versionProperty = _db.Entry(dashboard).Property(d => d.LayoutVersion);
        // The WHERE clause compares against the original value: pin it to what the caller expects.
        versionProperty.OriginalValue = expectedVersion;
        dashboard.LayoutVersion = expectedVersion + 1;
        versionProperty.IsModified = true;

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Drop the whole tracked graph (added/modified/removed children too), not just the parent entries,
            // so a later read in this scope re-queries the stored state. The context is request-scoped.
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private IQueryable<UserDashboard> WithChildren()
        => _db.UserDashboards.Include(d => d.Widgets).ThenInclude(w => w.Layouts);

    /// <summary>Assigns missing ids and propagates the dashboard's TenantId to children that have none.</summary>
    public static void PrepareGraph(UserDashboard dashboard)
    {
        if (dashboard.Id == Guid.Empty) dashboard.Id = Guid.NewGuid();
        foreach (var widget in dashboard.Widgets)
        {
            if (widget.Id == Guid.Empty) widget.Id = Guid.NewGuid();
            if (widget.TenantId == Guid.Empty) widget.TenantId = dashboard.TenantId;
            foreach (var layout in widget.Layouts)
            {
                if (layout.Id == Guid.Empty) layout.Id = Guid.NewGuid();
                if (layout.TenantId == Guid.Empty) layout.TenantId = dashboard.TenantId;
            }
        }
    }

    private static void AssignMissingIds(UserDashboard dashboard) => PrepareGraph(dashboard);
}
