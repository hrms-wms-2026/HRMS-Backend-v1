namespace ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

/// <summary>
/// Delegate task planning (2026-11-11), "Task form reads — least privilege": the only tasks and projects a planning
/// delegate may read for a principal.
/// </summary>
public interface IDelegateScopeRepository
{
    /// <summary>The task is assigned to the employee, or is the parent of a task assigned to them.</summary>
    Task<bool> IsAssignedOrParentOfAssignedAsync(Guid tenantId, Guid employeeId, Guid taskId, CancellationToken ct = default);

    /// <summary>The project has a task currently assigned to the employee.</summary>
    Task<bool> HasAssignedTaskInProjectAsync(Guid tenantId, Guid employeeId, Guid projectId, CancellationToken ct = default);
}
