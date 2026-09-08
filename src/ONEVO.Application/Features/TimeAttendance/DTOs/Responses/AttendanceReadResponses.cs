using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record AllowedClockInMethods(
    bool Web,
    bool DesktopTray,
    bool Biometric,
    bool PhotoRequired,
    bool LocationRequired,
    int? AllowedRadiusMeters);

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
    bool IsOverBreakAllowance = false);

public sealed record TimelineEvent(
    string EventType,
    DateTimeOffset Timestamp,
    string Source);

public sealed record AttendanceDayDetailResponse(
    AttendanceHistoryRow Summary,
    IReadOnlyList<TimelineEvent> TimelineEvents,
    ActivityDailySummaryDto? DailyActivity);
