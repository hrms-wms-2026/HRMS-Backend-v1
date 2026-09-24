namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record WorkAreaChangeApproverResponse(
    Guid EmployeeId,
    Guid UserId,
    string DisplayName,
    string? PositionName);

public sealed record WorkAreaChangeRequestResponse(
    Guid Id,
    Guid EmployeeId,
    Guid LegalEntityId,
    string RequesterDisplayName,
    string Timezone,
    DateOnly Date,
    Guid? CurrentWorkModeId,
    string CurrentWorkModeName,
    Guid? RequestedWorkModeId,
    string RequestedWorkModeName,
    string Reason,
    string Status,
    DateTimeOffset RequestedAt,
    Guid? ReviewedById,
    string? ReviewerDisplayName,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment,
    WorkAreaChangeApproverResponse? Receiver);

public sealed record WorkAreaChangeRequestPreviewResponse(
    DateOnly Date,
    string Timezone,
    Guid? CurrentWorkModeId,
    string CurrentWorkModeName,
    Guid RequestedWorkModeId,
    string RequestedWorkModeName,
    string Reason,
    WorkAreaChangeApproverResponse Receiver);
