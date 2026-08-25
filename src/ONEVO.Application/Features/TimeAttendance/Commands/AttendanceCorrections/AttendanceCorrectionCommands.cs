using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.AttendanceCorrections;

public sealed record PreviewAttendanceCorrectionCommand(
    DateOnly WorkDate,
    string CorrectionType,
    DateTimeOffset? RequestedClockInAt,
    DateTimeOffset? RequestedClockOutAt,
    IReadOnlyList<AttendanceCorrectionInputBreak>? RequestedBreaks,
    string Reason,
    string? Notes) : IRequest<Result<AttendanceCorrectionPreviewResponse>>;

public sealed record RequestAttendanceCorrectionCommand(
    DateOnly WorkDate,
    string CorrectionType,
    DateTimeOffset? RequestedClockInAt,
    DateTimeOffset? RequestedClockOutAt,
    IReadOnlyList<AttendanceCorrectionInputBreak>? RequestedBreaks,
    string Reason,
    string? Notes) : IRequest<Result<AttendanceCorrectionResponse>>;

public sealed record ApproveAttendanceCorrectionCommand(Guid Id, string? ReviewComment)
    : IRequest<Result<AttendanceCorrectionResponse>>;

public sealed record RejectAttendanceCorrectionCommand(Guid Id, string? ReviewComment)
    : IRequest<Result<AttendanceCorrectionResponse>>;

public sealed record CancelAttendanceCorrectionCommand(Guid Id)
    : IRequest<Result<AttendanceCorrectionResponse>>;

public sealed record AttendanceCorrectionInputBreak(
    DateTimeOffset BreakStart,
    DateTimeOffset BreakEnd,
    string BreakType);
