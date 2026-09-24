using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;

public sealed class EfOffboardingTaskBypassRequestRepository(ApplicationDbContext db) : IOffboardingTaskBypassRequestRepository
{
    public Task<OffboardingTaskBypassRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.OffboardingTaskBypassRequests.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<bool> HasPendingForTaskAsync(Guid tenantId, Guid employeeChecklistTaskId, CancellationToken ct = default)
        => db.OffboardingTaskBypassRequests.AnyAsync(x =>
            x.TenantId == tenantId && x.EmployeeChecklistTaskId == employeeChecklistTaskId && x.Status == BypassRequestStatuses.Pending, ct);

    public Task<IReadOnlyList<OffboardingTaskBypassRequest>> ListPendingByApproverAsync(Guid tenantId, Guid approverId, CancellationToken ct = default)
        => db.OffboardingTaskBypassRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ApproverId == approverId && x.Status == BypassRequestStatuses.Pending)
            .OrderBy(x => x.RequestedAt)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<OffboardingTaskBypassRequest>)t.Result, ct);

    public Task AddAsync(OffboardingTaskBypassRequest request, CancellationToken ct = default)
        => db.OffboardingTaskBypassRequests.AddAsync(request, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
