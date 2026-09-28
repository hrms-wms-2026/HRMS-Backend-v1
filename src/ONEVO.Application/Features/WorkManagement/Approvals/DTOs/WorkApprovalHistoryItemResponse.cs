namespace ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

public sealed record WorkApprovalHistoryItemResponse(
    Guid Id,
    Guid ObjectiveId,
    string Kind,
    string Status,
    string Direction,
    string SubjectTitle,
    string? Detail,
    Guid RequestedById,
    string RequestedByName,
    Guid ApproverId,
    string ApproverName,
    Guid? DecidedById,
    string? DecidedByName,
    string? DecisionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);
