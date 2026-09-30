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

/// <summary>
/// Buckets an employee's period tasks with the same rules as GetMyTaskProgressQueryHandler
/// (completed = status marks complete OR progress >= 100), plus on-time and story-point totals.
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

            if (row.MarksTaskComplete || row.ProgressPercent >= 100)
            {
                completed++;
                pointsCompleted += points;
                if (row.DueDate is { } due && row.CompletedAt is { } completedAt)
                {
                    withDue++;
                    if (DateOnly.FromDateTime(completedAt.UtcDateTime) <= due)
                        onTime++;
                }
            }
            else if (row.DueDate is { } dueDate && dueDate < asOf)
                overdue++;
            else if (row.ProgressPercent > 0)
                inProgress++;
            else
                notStarted++;
        }

        return new TaskPeriodStats(
            rows.Count, completed, inProgress, overdue, notStarted, onTime, withDue, pointsAssigned, pointsCompleted);
    }

    /// <summary>Rounded whole percentage; 0 when the whole is 0.</summary>
    public static int Percent(int part, int whole) =>
        whole == 0 ? 0 : (int)Math.Round(part * 100.0 / whole, MidpointRounding.AwayFromZero);
}
