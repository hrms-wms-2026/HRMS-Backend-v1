namespace ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;

/// <summary>Task-estimate bucket, identical to the wm_module_hour_summaries trigger: Completed =
/// status MarksTaskComplete or ProgressPercent &gt;= 100; otherwise InProgress when ProgressPercent
/// &gt; 0; otherwise NotStarted. The numeric order is the "status" sort order.</summary>
public enum LedTaskBucket
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
}

public enum LedEmployeeHoursSort
{
    TotalHours,
    CompletedHours,
    InProgressHours,
    NotStartedHours,
    Name,
}

public enum LedProgressTaskSort
{
    EstimatedHours,
    DueDate,
    Title,
    Status,
}

/// <summary>Inclusive DueDate window of the Team Progress period switch. When a window is given,
/// tasks without a DueDate are excluded.</summary>
public sealed record LedDueWindow(DateOnly From, DateOnly To);

/// <param name="ObjectiveIds">The (already scope-checked) Modules whose top-level tasks count.</param>
/// <param name="Search">Employee first/last name, case-insensitive; null/blank = no filter.</param>
/// <param name="DueWindow">Only tasks due inside this window; null = all time.</param>
public sealed record LedEmployeeHoursFilter(
    IReadOnlyCollection<Guid> ObjectiveIds, string? Search, LedEmployeeHoursSort Sort, bool Descending, int Skip, int Take,
    LedDueWindow? DueWindow = null);

public sealed record LedEmployeeHoursRow(
    Guid EmployeeId, string DisplayName, decimal CompletedHours, decimal InProgressHours, decimal NotStartedHours,
    decimal TotalHours, int TaskCount, int SharedTaskCount, decimal SharedHours);

public sealed record LedUnassignedHoursRow(
    decimal CompletedHours, decimal InProgressHours, decimal NotStartedHours, decimal TotalHours, int TaskCount);

/// <param name="Search">Task title or ShortId, case-insensitive.</param>
/// <param name="EmployeeId">Only tasks assigned to this employee.</param>
/// <param name="DueWindow">Only tasks due inside this window; null = all time.</param>
public sealed record LedProgressTaskFilter(
    IReadOnlyCollection<Guid> ObjectiveIds, string? Search, LedTaskBucket? Status, Guid? EmployeeId,
    LedProgressTaskSort Sort, bool Descending, int Skip, int Take, LedDueWindow? DueWindow = null);

public sealed record LedProgressTaskAssigneeRow(Guid EmployeeId, string DisplayName);

public sealed record LedProgressTaskRow(
    Guid TaskId, string ShortId, string Title, LedTaskBucket Bucket, string StatusName, decimal EstimatedHours,
    DateOnly? DueDate, Guid ProjectId, string ProjectName, Guid ObjectiveId, string ObjectiveTitle,
    IReadOnlyList<LedProgressTaskAssigneeRow> Assignees, int AssignmentCount);

/// <summary>Period totals: hours and task counts per bucket; UnestimatedTaskCount = tasks with no
/// estimate (counted as 0 h but still counted as tasks).</summary>
public sealed record LedPeriodTotalsRow(
    decimal CompletedHours, decimal InProgressHours, decimal NotStartedHours, decimal TotalHours,
    int TaskCount, int CompletedTaskCount, int InProgressTaskCount, int NotStartedTaskCount, int UnestimatedTaskCount)
{
    public static readonly LedPeriodTotalsRow Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record LedPagedRows<T>(IReadOnlyList<T> Rows, int TotalCount);

/// <summary>Team Progress detail reads (My Team spec §8.3). Top-level, non-deleted tasks of the given
/// Modules only. Every filter, aggregate, sort and page runs in the database.</summary>
public interface ILedProgressReadRepository
{
    /// <summary>Per-assignee hour totals; a multi-assignee task counts in full for each assignee.</summary>
    Task<LedPagedRows<LedEmployeeHoursRow>> ListEmployeeHoursAsync(Guid tenantId, LedEmployeeHoursFilter filter, CancellationToken ct = default);

    /// <summary>Totals of the tasks that have no assignment at all (inside <paramref name="dueWindow"/> when given).</summary>
    Task<LedUnassignedHoursRow> GetUnassignedHoursAsync(
        Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, LedDueWindow? dueWindow, CancellationToken ct = default);

    Task<LedPagedRows<LedProgressTaskRow>> ListTasksAsync(Guid tenantId, LedProgressTaskFilter filter, CancellationToken ct = default);

    /// <summary>Team totals of the tasks due inside <paramref name="dueWindow"/>, in one grouped query.</summary>
    Task<LedPeriodTotalsRow> GetPeriodTotalsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, LedDueWindow dueWindow, CancellationToken ct = default);
}
