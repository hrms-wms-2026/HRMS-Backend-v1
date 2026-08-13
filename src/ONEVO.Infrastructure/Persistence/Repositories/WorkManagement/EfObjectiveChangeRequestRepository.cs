using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfObjectiveChangeRequestRepository : IObjectiveChangeRequestRepository
{
    private readonly ApplicationDbContext _db;

    public EfObjectiveChangeRequestRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ObjectiveChangeRequest request, CancellationToken ct = default)
    {
        await _db.ObjectiveChangeRequests.AddAsync(request, ct);
    }

    public async Task<ObjectiveChangeRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.ObjectiveChangeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);
    }

    public async Task<bool> HasPendingForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
    {
        return await _db.ObjectiveChangeRequests
            .AsNoTracking()
            .AnyAsync(r => r.TenantId == tenantId && r.ObjectiveId == objectiveId && r.Status == ObjectiveChangeRequestStatuses.Pending, ct);
    }

    public async Task<IReadOnlyList<ObjectiveChangeRequest>> ListPendingForApproverAsync(Guid tenantId, Guid reportingManagerId, CancellationToken ct = default)
    {
        return await _db.ObjectiveChangeRequests
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ReportingManagerId == reportingManagerId && r.Status == ObjectiveChangeRequestStatuses.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public void Update(ObjectiveChangeRequest request)
    {
        _db.ObjectiveChangeRequests.Update(request);
    }
}
