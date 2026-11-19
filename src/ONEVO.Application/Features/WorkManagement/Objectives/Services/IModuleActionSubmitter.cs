using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

/// <summary>Applied = the change is in (Module is the tracked, updated row); otherwise ApprovalRequestId is set.</summary>
public sealed record ModuleActionOutcome(bool Applied, Guid? ApprovalRequestId, Objective? Module);

/// <summary>
/// The shared "through the engine" flow of every Module action: submits the action to
/// IWorkApprovalEngine at the Module's creator position (CreatorPositionObjectiveId, else its parent).
/// Direct → reloads the Module tracked, runs <c>apply</c>, notifies the head and the parent's owner.
/// Pending → the engine has stored the request and notified the approver. Saves inside one transaction.
/// Validation and permission checks stay in the callers.
/// </summary>
public interface IModuleActionSubmitter
{
    Task<Result<ModuleActionOutcome>> SubmitAsync(
        Guid tenantId, Guid actorEmployeeId, Objective module, string actionType, object? input,
        Func<Objective, CancellationToken, Task<Result>> apply, IReadOnlyCollection<Guid>? extraRecipients = null,
        CancellationToken ct = default);
}
