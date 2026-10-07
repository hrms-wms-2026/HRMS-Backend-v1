using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>The single source of truth for "how long after a decision can it be reverted" - both the
/// revert command's authorization check and the response mapper's canRevert/revertableUntil fields
/// read this, so the two can never disagree.</summary>
public static class ApprovalRevertWindow
{
    public const int Minutes = 30;

    /// <summary>True only for the viewer who decided this request, only while Approved/Rejected, only
    /// within Minutes of DecidedAt. Shared by WorkApprovalRequestMapper and GetApprovalDetailQueryHandler
    /// so the two can never disagree on what's revertable.</summary>
    public static (bool CanRevert, DateTimeOffset? RevertableUntil) ComputeCanRevert(WorkApprovalRequest request, Guid viewerEmployeeId)
    {
        var isDecided = request.Status == WorkApprovalRequestStatuses.Approved || request.Status == WorkApprovalRequestStatuses.Rejected;
        var revertableUntil = isDecided ? request.DecidedAt?.AddMinutes(Minutes) : null;
        var canRevert = isDecided && request.DecidedByEmployeeId == viewerEmployeeId
            && revertableUntil is { } until && until >= DateTimeOffset.UtcNow;
        return (canRevert, revertableUntil);
    }
}
