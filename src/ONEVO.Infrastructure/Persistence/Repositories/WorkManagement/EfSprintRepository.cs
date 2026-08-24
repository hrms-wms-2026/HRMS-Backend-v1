using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfSprintRepository : ISprintRepository
{
    private readonly ApplicationDbContext _db;

    public EfSprintRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Sprint sprint, CancellationToken ct = default)
        => await _db.Sprints.AddAsync(sprint, ct);

    public async Task<Sprint?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public async Task<Sprint?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Sprints.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public async Task<IReadOnlyList<Sprint>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.Sprints.AsNoTracking().Where(s => s.TenantId == tenantId && s.ObjectiveId == objectiveId).ToListAsync(ct);

    public async Task<IReadOnlyList<Sprint>> GetActiveByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.Sprints.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ObjectiveId == objectiveId && s.Status == SprintStatuses.Active)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Sprint>> GetByStatusAsync(string status, CancellationToken ct = default)
        => await _db.Sprints.Where(s => s.Status == status && !s.IsManuallyOverridden).ToListAsync(ct);

    public void Update(Sprint sprint) => _db.Sprints.Update(sprint);
}
