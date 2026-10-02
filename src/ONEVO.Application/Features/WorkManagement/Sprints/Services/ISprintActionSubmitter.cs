using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

/// <summary>Applied = the change is in (Sprint is the created/updated row, null after a delete); otherwise ApprovalRequestId is set.</summary>
public sealed record SprintActionOutcome(bool Applied, Guid? ApprovalRequestId, Sprint? Sprint);

/// <summary>One Sprint action as the submitter sees it. Sprint is null for a create.</summary>
public sealed record SprintActionRequest(
    Guid TenantId, Guid ActorEmployeeId, Guid ProjectId, Sprint? Sprint, string ActionType, string Title,
    object? Input, Guid? CreatorPositionObjectiveId = null, bool ForceDirect = false);

/// <summary>
/// The shared "through the engine" flow of every Sprint action. Submits to IWorkApprovalEngine with
/// the project root as TargetModule and the sprint's creator position (root if unset). Direct → runs
/// <c>apply</c> and notifies the position holder and the sprint's creator; Pending → the engine has
/// stored the request and notified the approver. Saves inside one transaction.
/// </summary>
public interface ISprintActionSubmitter
{
    /// <summary>Spec: the Module closest to the root the employee owns, else the one closest to the
    /// root they are an active member of, else the project root.</summary>
    Task<Result<Guid>> ResolveCreatorPositionAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default);

    Task<Result<SprintActionOutcome>> SubmitAsync(
        SprintActionRequest action, Func<CancellationToken, Task<Result<Sprint?>>> apply, CancellationToken ct = default);
}
