using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskStatusChangeRequestRepository : ITaskStatusChangeRequestRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskStatusChangeRequestRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskStatusChangeRequest request, CancellationToken ct = default)
        => await _db.TaskStatusChangeRequests.AddAsync(request, ct);

    public async Task<TaskStatusChangeRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskStatusChangeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<TaskStatusChangeRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskStatusChangeRequests.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<IReadOnlyList<TaskStatusChangeRequest>> ListPendingForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.TaskStatusChangeRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId && r.Status == TaskStatusChangeRequestStatuses.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TaskStatusChangeRequest>> ListTrackedPendingForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.TaskStatusChangeRequests
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId && r.Status == TaskStatusChangeRequestStatuses.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public void Update(TaskStatusChangeRequest request) => _db.TaskStatusChangeRequests.Update(request);
}
