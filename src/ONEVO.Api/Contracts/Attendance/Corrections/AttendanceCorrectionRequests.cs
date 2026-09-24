namespace ONEVO.Api.Contracts.Attendance.Corrections;

public sealed record AttendanceCorrectionBreakRequest(
    DateTimeOffset BreakStart,
    DateTimeOffset BreakEnd,
    string BreakType);

public sealed record RequestAttendanceCorrectionRequest(
    DateOnly WorkDate,
    string CorrectionType,
    DateTimeOffset? RequestedClockInAt,
    DateTimeOffset? RequestedClockOutAt,
    IReadOnlyList<AttendanceCorrectionBreakRequest>? RequestedBreaks,
    string Reason,
    string? Notes);

public sealed record ReviewAttendanceCorrectionRequest(string? ReviewComment);
