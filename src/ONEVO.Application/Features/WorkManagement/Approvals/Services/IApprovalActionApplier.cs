using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed record ApprovalApplyContext(WorkApprovalRequest Request, string PayloadJson, Guid DeciderEmployeeId);

public enum ApplyOutcomeKind { Applied, Stale, Invalid }

public sealed record ApplyOutcome(ApplyOutcomeKind Kind, string? Error = null)
{
    public static ApplyOutcome Applied { get; } = new(ApplyOutcomeKind.Applied);
    /// <summary>Target deleted, or changed since Request.TargetUpdatedAtSnapshot - nothing applied.</summary>
    public static ApplyOutcome Stale { get; } = new(ApplyOutcomeKind.Stale);
    /// <summary>The (possibly approver-edited) payload fails validation - nothing applied, the request stays pending.</summary>
    public static ApplyOutcome Invalid(string error) => new(ApplyOutcomeKind.Invalid, error);
}

/// <summary>
/// Applies one approved Work Management action type. One implementation per WorkActionTypes value,
/// registered in DI as IApprovalActionApplier. Runs inside the decide command's transaction and must
/// not call SaveChangesAsync.
/// </summary>
public interface IApprovalActionApplier
{
    string ActionType { get; }
    Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct);
}

public interface IApprovalActionApplierRegistry
{
    IApprovalActionApplier? Find(string actionType);
}
