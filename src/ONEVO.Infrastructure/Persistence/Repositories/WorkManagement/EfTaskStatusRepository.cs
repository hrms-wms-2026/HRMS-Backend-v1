using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskStatusRepository : ITaskStatusRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskStatusRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskStatusEntity status, CancellationToken ct = default)
        => await _db.TaskStatuses.AddAsync(status, ct);

    public async Task AddRangeAsync(IReadOnlyList<TaskStatusEntity> statuses, CancellationToken ct = default)
        => await _db.TaskStatuses.AddRangeAsync(statuses, ct);

    public async Task<IReadOnlyList<TaskStatusEntity>> GetProjectTemplateAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.TaskStatuses.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ProjectId == projectId && s.ObjectiveId == null)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TaskStatusEntity>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.TaskStatuses.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ObjectiveId == objectiveId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(ct);

    public async Task<TaskStatusEntity?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskStatuses.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public void Update(TaskStatusEntity status) => _db.TaskStatuses.Update(status);
    public void Remove(TaskStatusEntity status) => _db.TaskStatuses.Remove(status);
}
