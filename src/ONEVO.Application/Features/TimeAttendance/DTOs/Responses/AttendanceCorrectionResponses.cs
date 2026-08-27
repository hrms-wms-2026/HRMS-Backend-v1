namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record AttendanceCorrectionApproverResponse(
    Guid EmployeeId,
    Guid UserId,
    string DisplayName,
    string? PositionName);

public sealed record AttendanceCorrectionResponse(
    Guid Id,
    Guid EmployeeId,
    Guid LegalEntityId,
    string Timezone,
    DateOnly WorkDate,
    string CorrectionType,
    DateTimeOffset? RequestedClockInAt,
    DateTimeOffset? RequestedClockOutAt,
    IReadOnlyList<AttendanceCorrectionBreakResponse>? RequestedBreaks,
    string Reason,
    string? Notes,
    string Status,
    bool ApprovalRequired,
    Guid RequestedById,
    Guid? ReviewedById,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment,
    AttendanceCorrectionApproverResponse? Approver,
    string? RequesterDisplayName);

public sealed record AttendanceCorrectionBreakResponse(
    DateTimeOffset BreakStart,
    DateTimeOffset BreakEnd,
    string BreakType);

public sealed record AttendanceCorrectionPreviewResponse(
    bool ApprovalRequired,
    AttendanceCorrectionApproverResponse? Approver);
