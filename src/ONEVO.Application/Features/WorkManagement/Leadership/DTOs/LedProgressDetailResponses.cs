using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Leadership.DTOs;

/// <summary>One assignee's task-estimate hours inside the caller's led scope (Team Progress detail).
/// A multi-assignee task counts its FULL estimate for every assignee, so employee totals can add
/// up to more than the team total; SharedTaskCount/SharedHours are the part of this row that comes
/// from such shared tasks.</summary>
public sealed record LedEmployeeHoursItem(
    Guid EmployeeId,
    string DisplayName,
    decimal CompletedHours,
    decimal InProgressHours,
    decimal NotStartedHours,
    decimal TotalHours,
    int TaskCount,
    int SharedTaskCount,
    decimal SharedHours);

/// <summary>Tasks in scope with no assignee at all - never attributed to anyone.</summary>
public sealed record LedUnassignedHours(
    decimal CompletedHours,
    decimal InProgressHours,
    decimal NotStartedHours,
    decimal TotalHours,
    int TaskCount)
{
    public static readonly LedUnassignedHours Zero = new(0, 0, 0, 0, 0);
}

/// <summary>The standard paged shape plus the scope's <see cref="Unassigned"/> totals.</summary>
public sealed class LedEmployeeHoursPage(
    IReadOnlyList<LedEmployeeHoursItem> items, int pageNumber, int pageSize, int totalCount, LedUnassignedHours unassigned)
    : PagedResult<LedEmployeeHoursItem>(items, pageNumber, pageSize, totalCount)
{
    public LedUnassignedHours Unassigned { get; } = unassigned;
}

/// <summary>GET led-progress/period: team totals of the top-level in-scope tasks due in the period.
/// From/To are the inclusive window (serialized yyyy-MM-dd). Hours are task estimates; a task with
/// no estimate counts 0 h and is also counted in UnestimatedTaskCount.</summary>
/// <param name="Period">week | month</param>
public sealed record LedPeriodProgressResponse(
    string Period,
    DateOnly From,
    DateOnly To,
    decimal CompletedHours,
    decimal InProgressHours,
    decimal NotStartedHours,
    decimal TotalHours,
    int TaskCount,
    int CompletedTaskCount,
    int InProgressTaskCount,
    int NotStartedTaskCount,
    int UnestimatedTaskCount);

/// <summary>One ISO week (Monday..Sunday, serialized yyyy-MM-dd) of the weekly-completion chart:
/// estimated hours (null estimate = 0) and count of the in-scope top-level tasks that are done now
/// and whose CompletedAt UTC date falls in the week.</summary>
public sealed record LedWeeklyCompletionWeek(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    decimal CompletedHours,
    int CompletedTaskCount);

/// <summary>GET led-progress/weekly-completion. Weeks are oldest first, the last is the current
/// (partial) week, all present even at 0. AllocatedHours is the (narrowed) led scope's allocation -
/// the same number as led-progress's hours.allocatedHours when unfiltered. Undated* are done tasks
/// with no CompletedAt: reported, never guessed into a week.</summary>
public sealed record LedWeeklyCompletionResponse(
    IReadOnlyList<LedWeeklyCompletionWeek> Weeks,
    decimal AllocatedHours,
    decimal UndatedCompletedHours,
    int UndatedCompletedTaskCount);

/// <param name="Status">completed | inProgress | notStarted (same classification as wm_module_hour_summaries).</param>
/// <param name="StatusName">The task's configured status name.</param>
public sealed record LedProgressTaskItem(
    Guid TaskId,
    string ShortId,
    string Title,
    string Status,
    string StatusName,
    decimal EstimatedHours,
    DateOnly? DueDate,
    Guid ProjectId,
    string ProjectName,
    Guid ObjectiveId,
    string ObjectiveTitle,
    IReadOnlyList<LedTaskAssignee> Assignees,
    bool IsShared);

/// <summary>The previous period over the same elapsed span: planned task minutes elapsed by then, clocked minutes and
/// coverage (null without a plan).</summary>
public sealed record LedTimeComparison(int PlannedMinutes, int ClockedMinutes, decimal? CoveragePercent);

/// <summary>A day (week period) or an ISO week clipped to the month (month period). Minutes, never estimate hours.</summary>
public sealed record LedTimeBucket(DateOnly From, DateOnly To, int PlannedMinutes, int ScheduledMinutes, int UnscheduledMinutes);

/// <summary>Workload ranking by planned commitment (in-scope Planner blocks). ExpectedMinutes is null when the person's legal
/// entity has no working hours; utilization is not capped. More workload is not better performance.</summary>
public sealed record LedWorkloadRow(Guid EmployeeId, string DisplayName, int Rank, int PlannedMinutes, int ClockedMinutes,
    int UnscheduledMinutes, int? ExpectedMinutes, decimal? UtilizationPercent);

/// <param name="Kind">no_plan | outside_slot.</param>
public sealed record LedUnscheduledRow(Guid EmployeeId, string DisplayName, Guid TaskId, string ShortId, string Title,
    DateTimeOffset Start, DateTimeOffset End, int Minutes, string Kind);

/// <summary>Team Progress planned vs worked (My Team widgets design 2026-10-29, section 6.1): clocked task time and Planner
/// blocks on the led scope's tasks, each person's minutes on their own local days. PlannedMinutes is the whole period's
/// commitment; CoveragePercent = scheduled ÷ planned task minutes elapsed by now. Planner off: every plan figure is 0 and
/// coverage null, clocked still filled. No allocation figure is read or changed here.</summary>
public sealed record LedTimeProgressResponse(string Period, DateOnly From, DateOnly To, bool PlannerEnabled, DateTimeOffset AsOf,
    int PlannedMinutes, int ClockedMinutes, int ScheduledMinutes, int OutsideSlotMinutes, int NoPlanMinutes, decimal? CoveragePercent,
    int PeopleWithUnscheduledWork, LedTimeComparison Previous, IReadOnlyList<LedTimeBucket> Buckets,
    IReadOnlyList<LedWorkloadRow> Workload, IReadOnlyList<LedUnscheduledRow> Unscheduled, int UnscheduledTotal);
