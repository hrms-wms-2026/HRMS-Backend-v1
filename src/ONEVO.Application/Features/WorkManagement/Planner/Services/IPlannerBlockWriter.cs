using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>Plans a block for an employee other than the caller of the HTTP request, under the employee's own planner rules
/// (editable day, overlap, task assigned to them). Used when an approved work item request is planned for its requester.</summary>
public interface IPlannerBlockWriter
{
    Task<Result> PlanForAsync(Guid tenantId, Guid employeeId, Guid taskId, DateTimeOffset startAt, DateTimeOffset endAt,
        CancellationToken ct);

    /// <summary>Marks every block of the task, on anyone's planner, for removal with the caller's save (a reverted approval
    /// takes the task it created off the Planner).</summary>
    Task RemoveBlocksOfTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct);
}
