namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

/// <summary>Team week planned vs actual (My Team widgets design match 2026-11-02, section 7.4): the same population as Today's
/// Team Schedule, each person's minutes on their own local days. PlannerEnabled false = every plan field 0 and coverage null;
/// clocked minutes still filled. Previous = the same elapsed span of the week before.</summary>
public sealed record TeamScheduleWeekResponse(DateOnly WeekStart, DateOnly WeekEnd, bool PlannerEnabled, DateTimeOffset AsOf,
    IReadOnlyList<TeamWeekDay> Days, TeamWeekTotals Totals, TeamWeekTotals Previous, IReadOnlyList<TeamWeekMember> Members);

/// <summary>One weekday (weekStart … weekStart + 6), summed over the team.</summary>
public sealed record TeamWeekDay(DateOnly Date, int PlannedTaskMinutes, int PlannedMeetingMinutes, int ClockedMinutes,
    int ScheduledMinutes, int OutsideSlotMinutes, int NoPlanMinutes);

/// <param name="PlannedMinutes">Task + meeting minutes for the whole week (Previous: planned task minutes of the elapsed span).</param>
/// <param name="UnscheduledMinutes">Outside slot + no plan.</param>
/// <param name="CoveragePercent">min(100, scheduled ÷ planned task minutes elapsed so far × 100); null without elapsed plan or
/// with the Planner off.</param>
public sealed record TeamWeekTotals(int PlannedMinutes, int ClockedMinutes, int ScheduledMinutes, int UnscheduledMinutes,
    decimal? CoveragePercent);

public sealed record TeamWeekMember(Guid EmployeeId, string DisplayName, Guid? AvatarFileId, int PlannedMinutes,
    int ClockedMinutes, int ScheduledMinutes, int UnscheduledMinutes, decimal? CoveragePercent);
