using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfMonitorAlertRepository : IMonitorAlertRepository
{
    private readonly ApplicationDbContext _db;

    public EfMonitorAlertRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<MonitorAlert>> ListOpenTrackedForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.MonitorAlerts
            .Where(a => a.TenantId == tenantId && a.ProjectId == projectId && a.ResolvedAt == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MonitorAlert>> ListOpenForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.MonitorAlerts.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ProjectId == projectId && a.ResolvedAt == null)
            .OrderByDescending(a => a.FirstDetectedAt).ThenBy(a => a.Id)
            .ToListAsync(ct);

    public async Task AddAsync(MonitorAlert alert, CancellationToken ct = default)
        => await _db.MonitorAlerts.AddAsync(alert, ct);
}
