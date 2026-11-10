using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Dashboard;

public class EfDashboardTemplateRepository : IDashboardTemplateRepository
{
    private readonly ApplicationDbContext _db;

    public EfDashboardTemplateRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<DashboardTemplate>> ListForRolesAsync(
        Guid tenantId, string dashboardKey, IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default)
    {
        if (roleIds.Count == 0)
            return [];

        var ids = roleIds.ToList();
        return await WithChildren()
            .Where(t => t.TenantId == tenantId && t.DashboardKey == dashboardKey && t.IsActive
                && !t.IsNoRoleTemplate && t.RoleId != null && ids.Contains(t.RoleId.Value))
            .ToListAsync(ct);
    }

    public Task<DashboardTemplate?> GetNoRoleAsync(Guid tenantId, string dashboardKey, CancellationToken ct = default)
        => WithChildren().FirstOrDefaultAsync(
            t => t.TenantId == tenantId && t.DashboardKey == dashboardKey && t.IsActive && t.IsNoRoleTemplate, ct);

    // Soft-deleted rows (templates, widgets, layouts) are excluded by the global IsDeleted query filter, which also
    // applies to filtered includes; the explicit predicates keep that true if the filter ever changes.
    private IQueryable<DashboardTemplate> WithChildren() =>
        _db.DashboardTemplates
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Include(t => t.Widgets.Where(w => !w.IsDeleted))
            .ThenInclude(w => w.Layouts.Where(l => !l.IsDeleted));
}
