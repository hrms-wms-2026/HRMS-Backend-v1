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
        // Pick up widgets/layouts added to the tracked graph before touching the concurrency token.
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
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
                entry.State = EntityState.Detached;
            return false;
        }
    }

    private IQueryable<UserDashboard> WithChildren()
        => _db.UserDashboards.Include(d => d.Widgets).ThenInclude(w => w.Layouts);

    private static void AssignMissingIds(UserDashboard dashboard)
    {
        if (dashboard.Id == Guid.Empty) dashboard.Id = Guid.NewGuid();
        foreach (var widget in dashboard.Widgets)
        {
            if (widget.Id == Guid.Empty) widget.Id = Guid.NewGuid();
            foreach (var layout in widget.Layouts)
                if (layout.Id == Guid.Empty) layout.Id = Guid.NewGuid();
        }
    }
}
