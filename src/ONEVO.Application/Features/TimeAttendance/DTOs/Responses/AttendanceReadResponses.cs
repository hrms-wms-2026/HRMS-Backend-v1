using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record AllowedClockInMethods(
    bool Web,
    bool DesktopTray,
    bool Biometric,
    bool PhotoRequired,
    bool LocationRequired,
    int? AllowedRadiusMeters);

public sealed record AttendanceTodayBreakInterval(
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

public sealed record AttendanceTodayResponse(
    Guid EmployeeId,
    Guid LegalEntityId,
    DateOnly WorkDate,
    string Timezone,
    string ScheduleStatus,
    string PolicyStatus,
    bool IsWorkingDay,
    bool IsHoliday,
    string? HolidayName,
    string? ScheduledStartTime,
    string? ScheduledEndTime,
    int? RequiredWorkMinutes,
    int? BreakAllowanceMinutes,
    int BreakUsedMinutes,
    int? BreakRemainingMinutes,
    string BreakState,
    IReadOnlyList<AttendanceTodayBreakInterval> Breaks,
    string? ExpectedWorkMode,
    string AttendanceStatus,
    DateTimeOffset? ClockInAt,
    DateTimeOffset? ClockOutAt,
    int TotalWorkedMinutes,
    string? AttendanceSource,
    bool CanClockIn,
    bool CanClockOut,
    bool CanStartBreak,
    bool CanEndBreak,
    bool ShouldHaveClockedIn,
    bool CanViewCoveredEmployees,
    AllowedClockInMethods AllowedClockInMethods,
    IReadOnlyList<string> Messages,
    string? AttendanceStatusLabel = null,
    string? AttentionType = null,
    string? AttentionLabel = null,
    string? AttentionSeverity = null,
    int BreakOverageMinutes = 0,
    bool IsOverBreakAllowance = false,
    string? ExpectedWorkAreaSource = null,
    DateOnly? AttentionWorkDate = null);

public sealed record AttendanceHistoryEmployee(
    Guid EmployeeId,
    string DisplayName,
    string EmployeeNumber,
    string? Position,
    string? Department,
    Guid? AvatarFileId);

public sealed record AttendanceHistoryRow(
    Guid AttendanceRecordId,
    DateOnly WorkDate,
    AttendanceHistoryEmployee? Employee,
    DateTimeOffset? ClockInAt,
    DateTimeOffset? ClockOutAt,
    bool IsActive,
    int BreakMinutes,
    int TotalWorkedMinutes,
    string? ExpectedWorkMode,
    string? AttendanceSource,
    string Status,
    bool CanViewDetails,
    bool CanRequestCorrection,
    bool CanRequestWorkAreaChange,
    bool CanCorrect,
    string? StatusLabel = null,
    string? AttentionType = null,
    string? AttentionLabel = null,
    string? AttentionSeverity = null,
    int BreakOverageMinutes = 0,
    bool IsOverBreakAllowance = false,
    string? ScheduledStartTime = null,
    string? ScheduledEndTime = null,
    int? RequiredWorkMinutes = null,
    int FailedFaceChecks = 0,
    bool FaceCheckLetThrough = false,
    string? FaceCheckAlertStatus = null);

public sealed record AttendanceMonthlySummaryResponse(
    int WorkingDays,
    int DaysPresent,
    int LateArrivals,
    int EarlyDepartures,
    int MissingClockOuts);

public sealed record TimelineEvent(
    string EventType,
    DateTimeOffset Timestamp,
    string Source);

/// <summary>A tray check-in's captured location, read back for the attendance day-detail view.
/// Gated behind the same monitoring:read visibility as DailyActivity below.</summary>
public sealed record CheckInLocationDto(
    Guid Id,
    DateTimeOffset CheckedInAt,
    double? Latitude,
    double? Longitude,
    double? LocationAccuracy,
    string? LocationAddress);

/// <summary>One screenshot for the attendance day, with a short-lived Cloudflare read link.</summary>
public sealed record AttendanceDayScreenshotDto(
    Guid Id,
    DateTimeOffset CapturedAt,
    string EvidenceType,
    string TriggerType,
    string? Url);

/// <summary>One idle-time activity check. Url is set only when the employee chose Allow.</summary>
public sealed record AttendanceActivityCheckDto(
    Guid Id,
    DateTimeOffset PromptedAt,
    string Outcome,
    string? Url);

/// <summary>One clock-in/out face check on the day. Only returned for the employee's own day.</summary>
/// <param name="Outcome">"passed", "failed" or "overridden" (failed on the last try, let through for review).</param>
/// <param name="PhotoUrl">Short-lived signed URL of the photo kept for a failed check; null when none was kept.</param>
public sealed record AttendanceFaceCheckDto(
    Guid Id,
    DateTimeOffset At,
    string Purpose,
    string Outcome,
    string? FailureReason,
    string? PhotoUrl);

/// <summary>A face-check alert (identity case) raised on the employee's own day. The reviewer's
/// note is left out - this is the employee's view.</summary>
/// <param name="Status">"Open", "Acknowledged", "Escalated" or "Resolved".</param>
public sealed record AttendanceFaceCheckAlertDto(
    Guid Id,
    DateTimeOffset DetectedAt,
    string? Purpose,
    string Status,
    DateTimeOffset? ResolvedAt);

public sealed record AttendanceDayDetailResponse(
    AttendanceHistoryRow Summary,
    IReadOnlyList<TimelineEvent> TimelineEvents,
    ActivityDailySummaryDto? DailyActivity,
    IReadOnlyList<CheckInLocationDto> CheckIns,
    IReadOnlyList<AttendanceDayScreenshotDto>? Screenshots = null,
    IReadOnlyList<AttendanceActivityCheckDto>? ActivityChecks = null,
    IReadOnlyList<AttendanceFaceCheckDto>? FaceChecks = null,
    IReadOnlyList<AttendanceFaceCheckAlertDto>? FaceCheckAlerts = null);
