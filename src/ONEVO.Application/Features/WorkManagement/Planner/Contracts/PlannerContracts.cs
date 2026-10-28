namespace ONEVO.Application.Features.WorkManagement.Planner.Contracts;

public static class PlannerErrorCodes
{
    public const string Disabled = "planner_disabled";
    /// <summary>No longer returned (v1 "today only"); kept so older clients' error maps still compile against it.</summary>
    public const string NotToday = "planner_not_today";
    public const string DayReadOnly = "planner_day_read_only";
    public const string BeyondHorizon = "planner_beyond_horizon";
    public const string BlockOverlap = "planner_block_overlap";
    public const string TaskNotPlannable = "planner_task_not_plannable";
    public const string InvalidTime = "planner_invalid_time";
    public const string NotFound = "planner_not_found";
    public const string InvalidRange = "planner_invalid_range";
}

/// <param name="TaskId">A task assigned to the caller that is not a meeting.</param>
public sealed record CreatePlannedBlockRequest(Guid TaskId, DateTimeOffset StartAt, DateTimeOffset EndAt);

public sealed record MovePlannedBlockRequest(DateTimeOffset StartAt, DateTimeOffset EndAt);

/// <param name="ActualMinutes">The task's clocked minutes on that day (the same for every block of one task).</param>
/// <param name="StillAssigned">False once the task is no longer assigned to the employee; the block stays.</param>
/// <param name="OverlapsMeeting">The block overlaps a meeting the employee attends (a conflict to show).</param>
public sealed record PlannerBlockView(Guid Id, Guid TaskId, string TaskTitle, DateTimeOffset StartAt, DateTimeOffset EndAt,
    int PlannedMinutes, int ActualMinutes, bool StillAssigned, bool OverlapsMeeting);

/// <param name="Title">"Private meeting" for a manager viewing a private meeting (spec D17).</param>
public sealed record PlannerMeetingView(Guid TaskId, Guid SeriesId, DateTimeOffset OriginalStart, DateTimeOffset StartAt,
    DateTimeOffset EndAt, string Title, bool IsPrivate, bool Cancelled, bool Declined, int PlannedMinutes, int ActualMinutes);

public sealed record PlannerDueTaskView(Guid TaskId, string Title, DateOnly? DueDate, decimal? EstimatedHours);

/// <param name="ActualMinutes">Distinct tasks' clocked minutes plus ended meetings not declined.</param>
public sealed record PlannerTotals(int PlannedMinutes, int ActualMinutes);

/// <param name="Editable">True only for the employee's own today.</param>
/// <param name="AttendanceMinutes">The attendance clock for that day: the recorded worked minutes once clocked out, or the
/// time since clock-in minus breaks while still clocked in; 0 with no attendance record.</param>
public sealed record PlannerDayResponse(DateOnly Date, string Timezone, bool Editable, IReadOnlyList<PlannerBlockView> Blocks,
    IReadOnlyList<PlannerMeetingView> Meetings, IReadOnlyList<PlannerDueTaskView> DueTasks, PlannerTotals Totals,
    int AttendanceMinutes);

/// <summary>A day's working hours from the legal entity schedule, as wall-clock in the employee's zone (2026-10-27 design,
/// call C1).</summary>
/// <param name="BreakStart">Centred in the window and rounded down to 15 minutes (call C7); null with no break.</param>
/// <param name="WorkingMinutes">The window less the break; 0 on a non-working day.</param>
public sealed record PlannerWorkWindow(TimeOnly WorkStart, TimeOnly WorkEnd, int BreakMinutes, TimeOnly? BreakStart,
    bool IsWorkingDay, int WorkingMinutes);

/// <param name="ProjectId">For opening the task on its project board.</param>
/// <param name="ActualMinutes">The employee's clocked minutes on that task inside that day (as in v1).</param>
public sealed record PlannerRangeBlockView(Guid Id, Guid TaskId, Guid ProjectId, string TaskTitle, DateTimeOffset StartAt,
    DateTimeOffset EndAt, int PlannedMinutes, int ActualMinutes, bool StillAssigned);

/// <param name="Editable">The caller's own day from today up to the planning horizon.</param>
/// <param name="WorkWindow">Null when the legal entity has no usable schedule.</param>
/// <param name="BlockMinutes">The day's planned task minutes; the load the grid shows (blocks plus busy events, counted
/// once) is computed by the client (call C2).</param>
public sealed record PlannerRangeDay(DateOnly Date, bool Editable, PlannerWorkWindow? WorkWindow,
    IReadOnlyList<PlannerRangeBlockView> Blocks, int BlockMinutes, int WorkingMinutes);

/// <summary>GET /planner/me/range: the caller's days from <c>from</c> to <c>to</c>, at most 42.</summary>
public sealed record PlannerRangeResponse(string Timezone, DateOnly Today, IReadOnlyList<PlannerRangeDay> Days);

/// <summary>Why a task is in the Focus list (2026-10-27 design, section 4.2).</summary>
public static class PlannerFocusReasons
{
    public const string Overdue = "overdue";
    public const string DueToday = "due_today";
    public const string DueThisWeek = "due_this_week";
    public const string Backlog = "backlog";
}

/// <param name="LoggedMinutes">Everyone's clocked minutes on the task (call C4).</param>
/// <param name="RemainingMinutes">Estimate less logged, never below 0; null without an estimate.</param>
/// <param name="PlannedAheadMinutes">The caller's block minutes on this task from today on.</param>
/// <param name="FullyPlanned">Those blocks cover the remaining estimate; never true without an estimate.</param>
public sealed record PlannerFocusItem(Guid TaskId, Guid ProjectId, string ProjectName, string Title, string Priority, DateOnly? DueDate,
    int? EstimatedMinutes, int LoggedMinutes, int? RemainingMinutes, int PlannedAheadMinutes, DateOnly? FirstPlannedDate,
    int PlannedDayCount, bool FullyPlanned, string Reason);

/// <param name="Count">Every task in the group; <see cref="Items"/> keeps the first 50.</param>
public sealed record PlannerFocusGroup(string Reason, int Count, IReadOnlyList<PlannerFocusItem> Items);

/// <summary>GET /planner/me/focus: always the four groups, in the order overdue, due today, due this week, backlog.</summary>
/// <param name="WeekEnd">The Sunday of today's Monday-based week, in the caller's zone.</param>
public sealed record PlannerFocusResponse(DateOnly Today, DateOnly WeekEnd, IReadOnlyList<PlannerFocusGroup> Groups);
