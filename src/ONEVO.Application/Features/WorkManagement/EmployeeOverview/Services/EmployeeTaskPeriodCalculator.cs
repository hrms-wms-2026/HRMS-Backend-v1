using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;

public sealed record TaskPeriodStats(
    int Assigned,
    int Completed,
    int InProgress,
    int Overdue,
    int NotStarted,
    int OnTimeCompleted,
    int CompletedWithDueDate,
    int StoryPointsAssigned,
    int StoryPointsCompleted);

/// <summary>The four Work card buckets, in the order the task list shows them.</summary>
public static class EmployeeTaskBuckets
{
    public const string Overdue = "overdue";
    public const string NotStarted = "not_started";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
}

/// <summary>
/// Buckets an employee's period tasks with the same rules as GetMyTaskProgressQueryHandler
/// (completed = status marks complete OR progress >= 100), plus on-time and story-point totals.
/// Completion is judged as of <c>asOf</c>: a task finished after it (e.g. viewing a past month) still
/// counts as open - and so possibly overdue - in that period.
/// </summary>
public static class EmployeeTaskPeriodCalculator
{
    public static TaskPeriodStats Compute(IReadOnlyList<EmployeeTaskPeriodRow> rows, DateOnly asOf)
    {
        int completed = 0, inProgress = 0, overdue = 0, notStarted = 0;
        int onTime = 0, withDue = 0, pointsAssigned = 0, pointsCompleted = 0;

        foreach (var row in rows)
        {
            var points = row.StoryPoints ?? 0;
            pointsAssigned += points;

            switch (Bucket(row, asOf))
            {
                case EmployeeTaskBuckets.Completed:
                    completed++;
                    pointsCompleted += points;
                    if (row.DueDate is { } due && row.CompletedAt is { } completedAt)
                    {
                        withDue++;
                        if (DateOnly.FromDateTime(completedAt.UtcDateTime) <= due)
                            onTime++;
                    }
                    break;
                case EmployeeTaskBuckets.Overdue:
                    overdue++;
                    break;
                case EmployeeTaskBuckets.InProgress:
                    inProgress++;
                    break;
                default:
                    notStarted++;
                    break;
            }
        }

        return new TaskPeriodStats(
            rows.Count, completed, inProgress, overdue, notStarted, onTime, withDue, pointsAssigned, pointsCompleted);
    }

    /// <summary>Which Work card bucket a task falls in - the same precedence Compute counts with.</summary>
    public static string Bucket(EmployeeTaskPeriodRow row, DateOnly asOf) =>
        IsComplete(row, asOf) ? EmployeeTaskBuckets.Completed
        : IsOverdue(row, asOf) ? EmployeeTaskBuckets.Overdue
        : row.ProgressPercent > 0 ? EmployeeTaskBuckets.InProgress
        : EmployeeTaskBuckets.NotStarted;

    /// <summary>The overdue rule shared by the Overview counts and the drill-down list.</summary>
    public static bool IsOverdue(EmployeeTaskPeriodRow row, DateOnly asOf) =>
        !IsComplete(row, asOf) && row.DueDate is { } due && due < asOf;

    /// <summary>Whole days past due as of <c>asOf</c>; 0 when not overdue.</summary>
    public static int DaysOverdue(EmployeeTaskPeriodRow row, DateOnly asOf) =>
        IsOverdue(row, asOf) ? asOf.DayNumber - row.DueDate!.Value.DayNumber : 0;

    private static bool IsComplete(EmployeeTaskPeriodRow row, DateOnly asOf) =>
        (row.MarksTaskComplete || row.ProgressPercent >= 100)
        && (row.CompletedAt is not { } completedAt || DateOnly.FromDateTime(completedAt.UtcDateTime) <= asOf);

    /// <summary>Rounded whole percentage; 0 when the whole is 0.</summary>
    public static int Percent(int part, int whole) =>
        whole == 0 ? 0 : (int)Math.Round(part * 100.0 / whole, MidpointRounding.AwayFromZero);
}
