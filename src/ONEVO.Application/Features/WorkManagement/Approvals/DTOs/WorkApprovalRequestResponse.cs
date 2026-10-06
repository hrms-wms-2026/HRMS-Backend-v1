namespace ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

public sealed record WorkApprovalRequestResponse(
    Guid Id,
    Guid ProjectId,
    string ActionType,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    string Status,
    Guid RequestedByEmployeeId,
    string RequestedByName,
    Guid ApproverEmployeeId,
    string ApproverName,
    string PayloadJson,
    string? DecisionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    /// <summary>The target Module's allocated hours right now - set only for module.allocation_extend rows, so the
    /// approver sees "current -> requested".</summary>
    decimal? CurrentAllocatedHours,
    /// <summary>True only for the viewer who decided this request, only while Approved/Rejected, only
    /// within ApprovalRevertWindow.Minutes of DecidedAt.</summary>
    bool CanRevert,
    DateTimeOffset? RevertableUntil);
