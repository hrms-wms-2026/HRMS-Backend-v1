namespace ONEVO.Application.Features.WorkManagement.Planner.Contracts;

public static class PlannerErrorCodes
{
    public const string Disabled = "planner_disabled";
    public const string NotToday = "planner_not_today";
    public const string BlockOverlap = "planner_block_overlap";
    public const string TaskNotPlannable = "planner_task_not_plannable";
    public const string InvalidTime = "planner_invalid_time";
    public const string NotFound = "planner_not_found";
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
public sealed record PlannerDayResponse(DateOnly Date, string Timezone, bool Editable, IReadOnlyList<PlannerBlockView> Blocks,
    IReadOnlyList<PlannerMeetingView> Meetings, IReadOnlyList<PlannerDueTaskView> DueTasks, PlannerTotals Totals);
