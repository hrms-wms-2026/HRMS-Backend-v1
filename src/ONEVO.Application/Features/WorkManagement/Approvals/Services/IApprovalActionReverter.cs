using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed record ApprovalRevertContext(WorkApprovalRequest Request, Guid DeciderEmployeeId);

public enum RevertOutcomeKind { Reverted, Stale, Conflict, NotRevertable }

public sealed record RevertOutcome(RevertOutcomeKind Kind, string? Reason = null, Func<DateTimeOffset?>? ReadTargetUpdatedAt = null)
{
    /// <summary>readTargetUpdatedAt lets the caller re-baseline Request.TargetUpdatedAtSnapshot to the
    /// true post-revert, post-save value (the reverter holds the tracked target reference; the
    /// AuditableEntityInterceptor only stamps the real UpdatedAt during SaveChangesAsync, so the caller
    /// must read it back AFTER that save, not compute its own "now"). Null when the reverter touched no
    /// snapshot-checked target (e.g. task.create, which nulls TargetId instead).</summary>
    public static RevertOutcome Reverted(Func<DateTimeOffset?>? readTargetUpdatedAt = null) => new(RevertOutcomeKind.Reverted, null, readTargetUpdatedAt);
    /// <summary>The target was deleted or changed since the request was decided - nothing to undo.</summary>
    public static RevertOutcome Stale { get; } = new(RevertOutcomeKind.Stale);
    /// <summary>Reality moved on since apply in a way that makes undoing it unsafe (e.g. a member who was
    /// added has since been assigned work). The request stays Approved/Rejected; nothing is undone.</summary>
    public static RevertOutcome Conflict(string reason) => new(RevertOutcomeKind.Conflict, reason);
    /// <summary>This action type has no reverter, or this specific request has nothing to undo (e.g. it
    /// was Rejected, so nothing was ever applied).</summary>
    public static RevertOutcome NotRevertable(string reason) => new(RevertOutcomeKind.NotRevertable, reason);
}

/// <summary>
/// Undoes one approved Work Management action type - the mirror of IApprovalActionApplier. One
/// implementation per WorkActionTypes value that has something to undo, registered in DI as
/// IApprovalActionReverter. Runs inside the revert command's transaction and must not call
/// SaveChangesAsync.
/// </summary>
public interface IApprovalActionReverter
{
    string ActionType { get; }
    Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct);
}

public interface IApprovalActionReverterRegistry
{
    IApprovalActionReverter? Find(string actionType);
}
