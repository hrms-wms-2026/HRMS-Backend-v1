using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>Plans a block for an employee other than the caller of the HTTP request, under the employee's own planner rules
/// (editable day, overlap, task assigned to them). Used when an approved work item request is planned for its requester.</summary>
public interface IPlannerBlockWriter
{
    Task<Result> PlanForAsync(Guid tenantId, Guid employeeId, Guid taskId, DateTimeOffset startAt, DateTimeOffset endAt,
        CancellationToken ct);
}
