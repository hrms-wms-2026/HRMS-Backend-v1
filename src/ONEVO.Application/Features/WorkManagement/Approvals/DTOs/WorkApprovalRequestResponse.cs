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
    DateTimeOffset? DecidedAt);
