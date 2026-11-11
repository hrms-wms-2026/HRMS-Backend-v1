using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

public sealed class EfDelegateScopeRepository : IDelegateScopeRepository
{
    private readonly ApplicationDbContext _db;

    public EfDelegateScopeRepository(ApplicationDbContext db) => _db = db;

    public Task<bool> IsAssignedOrParentOfAssignedAsync(Guid tenantId, Guid employeeId, Guid taskId, CancellationToken ct = default)
        => _db.WorkTasks.AsNoTracking().AnyAsync(t => t.TenantId == tenantId && !t.IsDeleted
            && (t.Id == taskId || t.ParentTaskId == taskId)
            && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId), ct);

    public Task<bool> HasAssignedTaskInProjectAsync(Guid tenantId, Guid employeeId, Guid projectId, CancellationToken ct = default)
        => _db.WorkTasks.AsNoTracking().AnyAsync(t => t.TenantId == tenantId && !t.IsDeleted && t.ProjectId == projectId
            && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId), ct);
}
