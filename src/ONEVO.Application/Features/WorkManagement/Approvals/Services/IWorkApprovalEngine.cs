using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>
/// One Work Management action as the approval engine sees it.
/// - TargetModuleId: the Module the target lives in (for a Module target, the Module itself; for a
///   create, the Module being created into; for a Sprint, the project root Module).
/// - PositionModuleId: the target's CreatorPositionObjectiveId (for a create, the caller's choice
///   per spec §5.2 rule 1); null means "use the default".
/// - TargetUpdatedAt: the target's UpdatedAt at submit time, for stale detection.
/// </summary>
public sealed record WorkAction(
    Guid TenantId,
    Guid ProjectId,
    Guid ActorEmployeeId,
    string ActionType,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    Guid TargetModuleId,
    Guid? PositionModuleId,
    string PayloadJson,
    DateTimeOffset? TargetUpdatedAt);

public sealed record ApprovalDecision(bool IsDirect, Guid? ApprovalRequestId, Guid? ApproverEmployeeId)
{
    public static ApprovalDecision Direct { get; } = new(true, null, null);
    public static ApprovalDecision Pending(Guid requestId, Guid approverEmployeeId) => new(false, requestId, approverEmployeeId);
}

/// <summary>
/// The Work Management approval engine. Direct means "apply now": the caller applies the change and
/// calls IWorkNotificationEngine with Kind = Direct. Pending means a wm_approval_requests row was
/// added and the approver was notified. Never calls SaveChangesAsync - run it inside the caller's
/// transaction.
/// </summary>
public interface IWorkApprovalEngine
{
    Task<Result<ApprovalDecision>> SubmitAsync(WorkAction action, CancellationToken ct = default);
}
