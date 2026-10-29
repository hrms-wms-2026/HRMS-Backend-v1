namespace ONEVO.Application.Features.WorkManagement.WeeklyProgress.DTOs;

/// <summary>Weekly Work Progress widget (2026-10-28 design, section 8): this week and last week for the caller.</summary>
public sealed record WeeklyWorkProgressResponse(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    DateOnly Today,
    string Timezone,
    bool PlannerEnabled,
    WeekProgressDto ThisWeek,
    WeekProgressDto LastWeek,
    WeekTaskWeightsDto Tasks);

/// <summary>One Monday-Sunday week. Minutes are whole minutes; percentages have one decimal.</summary>
public sealed record WeekProgressDto(
    int ExpectedMinutes,
    int ExpectedToDateMinutes,
    int ScheduledTaskMinutes,
    int ScheduledMeetingMinutes,
    int ScheduledMinutes,
    int LoggedTaskMinutes,
    int CreditedMeetingMinutes,
    int WorkedMinutes,
    int AttendanceMinutes,
    int RemainingMinutes,
    decimal ProgressPercent,
    decimal PlanningCoveragePercent,
    decimal? ScheduleAdherencePercent,
    string Status,
    IReadOnlyList<WeekDayProgressDto> Days);

public sealed record WeekDayProgressDto(
    DateOnly Date,
    bool IsExpected,
    bool IsHoliday,
    int LeaveMinutes,
    int ExpectedMinutes,
    int ScheduledMinutes,
    int WorkedMinutes,
    int AttendanceMinutes);

/// <summary>Estimated hours of the week's tasks by status category; separate from worked hours on purpose.</summary>
public sealed record WeekTaskWeightsDto(decimal CompletedHours, decimal InProgressHours, decimal NotStartedHours, int UnestimatedCount);
