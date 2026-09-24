using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskCreationRequestRepository : ITaskCreationRequestRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskCreationRequestRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskCreationRequest request, CancellationToken ct = default)
        => await _db.TaskCreationRequests.AddAsync(request, ct);

    public async Task<TaskCreationRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskCreationRequests.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<TaskCreationRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskCreationRequests.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<IReadOnlyList<TaskCreationRequest>> GetPendingForOwnerEmployeeIdAsync(Guid tenantId, Guid ownerEmployeeId, CancellationToken ct = default)
        => await (
            from r in _db.TaskCreationRequests.AsNoTracking()
            join o in _db.Objectives.AsNoTracking() on r.ObjectiveId equals o.Id
            where r.TenantId == tenantId && r.Status == TaskCreationRequestStatuses.Pending && o.OwnerId == ownerEmployeeId
            select r
        ).ToListAsync(ct);

    public void Update(TaskCreationRequest request) => _db.TaskCreationRequests.Update(request);
}
