using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Onboarding.ServiceInterfaces;

/// <summary>Resolves a chosen position to a single concrete user id for a checklist template
/// task's assignedToId. Never auto-picks among multiple occupants. Not used at finalization
/// time: by then employeeId/userId are already concrete (see ChecklistTaskJsonContract.
/// ToEmployeeChecklistTasks).</summary>
public interface IChecklistTemplateAssigneeResolver
{
    Task<Result<Guid>> ResolveAsync(Guid tenantId, Guid positionId, CancellationToken ct = default);
}
