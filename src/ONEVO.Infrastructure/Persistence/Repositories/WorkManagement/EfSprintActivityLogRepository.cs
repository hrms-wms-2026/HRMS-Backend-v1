using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfSprintActivityLogRepository : ISprintActivityLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfSprintActivityLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(SprintActivityLog log, CancellationToken ct = default)
        => await _db.SprintActivityLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<SprintActivityLog>> GetForSprintAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default)
        => await _db.SprintActivityLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantId && log.SprintId == sprintId)
            .OrderBy(log => log.OccurredAt)
            .ToListAsync(ct);
}
