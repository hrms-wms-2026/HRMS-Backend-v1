using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskAssignmentRepository
{
    Task AddAsync(TaskAssignment assignment, CancellationToken ct = default);
    Task<IReadOnlyList<TaskAssignment>> GetByTaskIdAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>Bulk lookup for board/list rendering - avoids one query per task.</summary>
    Task<IReadOnlyList<TaskAssignment>> GetByTaskIdsAsync(IReadOnlyList<Guid> taskIds, CancellationToken ct = default);
    Task<TaskAssignment?> GetByTaskAndEmployeeAsync(Guid taskId, Guid employeeId, CancellationToken ct = default);

    /// <summary>True if this employee is assigned to any (non-deleted) task whose Module is this one.
    /// Used by ModuleMemberAddReverter to refuse reverting a membership grant once the member has
    /// already started real work under it.</summary>
    Task<bool> AnyForEmployeeInObjectiveAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);

    void Remove(TaskAssignment assignment);
}
