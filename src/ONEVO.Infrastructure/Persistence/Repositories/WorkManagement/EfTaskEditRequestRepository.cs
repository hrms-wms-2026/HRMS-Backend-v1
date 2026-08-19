using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskEditRequestRepository : ITaskEditRequestRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskEditRequestRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskEditRequest request, CancellationToken ct = default)
        => await _db.TaskEditRequests.AddAsync(request, ct);

    public async Task<TaskEditRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskEditRequests.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<TaskEditRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskEditRequests.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<IReadOnlyList<TaskEditRequest>> GetPendingForOwnerEmployeeIdAsync(Guid tenantId, Guid ownerEmployeeId, CancellationToken ct = default)
        => await (
            from r in _db.TaskEditRequests.AsNoTracking()
            join t in _db.WorkTasks.AsNoTracking() on r.TaskId equals t.Id
            join o in _db.Objectives.AsNoTracking() on t.ObjectiveId equals o.Id
            where r.TenantId == tenantId && r.Status == TaskEditRequestStatuses.Pending && o.OwnerId == ownerEmployeeId
            select r
        ).ToListAsync(ct);

    public void Update(TaskEditRequest request) => _db.TaskEditRequests.Update(request);
}
