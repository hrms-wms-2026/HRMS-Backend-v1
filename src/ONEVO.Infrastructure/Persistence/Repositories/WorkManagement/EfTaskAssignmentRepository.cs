using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskAssignmentRepository : ITaskAssignmentRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskAssignmentRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskAssignment assignment, CancellationToken ct = default)
        => await _db.TaskAssignments.AddAsync(assignment, ct);

    public async Task<IReadOnlyList<TaskAssignment>> GetByTaskIdAsync(Guid taskId, CancellationToken ct = default)
        => await _db.TaskAssignments.AsNoTracking().Where(a => a.TaskId == taskId).ToListAsync(ct);

    public async Task<IReadOnlyList<TaskAssignment>> GetByTaskIdsAsync(IReadOnlyList<Guid> taskIds, CancellationToken ct = default)
        => await _db.TaskAssignments.AsNoTracking().Where(a => taskIds.Contains(a.TaskId)).ToListAsync(ct);

    public async Task<TaskAssignment?> GetByTaskAndEmployeeAsync(Guid taskId, Guid employeeId, CancellationToken ct = default)
        => await _db.TaskAssignments.FirstOrDefaultAsync(a => a.TaskId == taskId && a.EmployeeId == employeeId, ct);

    public async Task<bool> AnyForEmployeeInObjectiveAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
        => await _db.TaskAssignments.AsNoTracking()
            .Join(_db.WorkTasks.AsNoTracking(), a => a.TaskId, t => t.Id, (a, t) => new { a, t })
            .AnyAsync(x => x.t.TenantId == tenantId && x.a.EmployeeId == employeeId && x.t.ObjectiveId == objectiveId, ct);

    public void Remove(TaskAssignment assignment) => _db.TaskAssignments.Remove(assignment);
}
