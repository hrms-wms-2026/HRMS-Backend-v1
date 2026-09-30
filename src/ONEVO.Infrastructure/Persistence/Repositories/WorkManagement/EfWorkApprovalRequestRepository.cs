using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfWorkApprovalRequestRepository : IWorkApprovalRequestRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkApprovalRequestRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(WorkApprovalRequest request, CancellationToken ct = default)
        => await _db.WorkApprovalRequests.AddAsync(request, ct);

    public async Task<WorkApprovalRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.WorkApprovalRequests.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<WorkApprovalRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.WorkApprovalRequests.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<bool> HasPendingAsync(Guid tenantId, string targetType, Guid targetId, string actionType, CancellationToken ct = default)
        => await _db.WorkApprovalRequests.AsNoTracking().AnyAsync(r =>
            r.TenantId == tenantId && r.TargetType == targetType && r.TargetId == targetId
            && r.ActionType == actionType && r.Status == WorkApprovalRequestStatuses.Pending, ct);

    public async Task<IReadOnlyList<WorkApprovalRequest>> ListByProjectAsync(
        Guid tenantId, Guid projectId, Guid? requestedByEmployeeId, string? status, CancellationToken ct = default)
    {
        var query = _db.WorkApprovalRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId);
        if (requestedByEmployeeId is { } requester)
            query = query.Where(r => r.RequestedByEmployeeId == requester);
        if (status is not null)
            query = query.Where(r => r.Status == status);

        // Sort client-side: the in-memory unit-test provider cannot ORDER BY DateTimeOffset, and the per-project
        // row count is small.
        var rows = await query.ToListAsync(ct);
        return rows.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<WorkApprovalRequest>> ListTrackedPendingByActionAsync(
        Guid tenantId, Guid projectId, string actionType, CancellationToken ct = default)
        => await _db.WorkApprovalRequests
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId
                && r.ActionType == actionType && r.Status == WorkApprovalRequestStatuses.Pending)
            .ToListAsync(ct);

    public void Update(WorkApprovalRequest request) => _db.WorkApprovalRequests.Update(request);
}
