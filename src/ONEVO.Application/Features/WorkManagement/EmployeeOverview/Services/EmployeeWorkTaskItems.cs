using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;

/// <summary>
/// Turns an employee's task rows into Work card list items: bucketed as of <c>asOf</c>, ordered overdue,
/// not started, in progress, completed; then by priority (critical first); then by due date (none last).
/// </summary>
public static class EmployeeWorkTaskItems
{
    private static readonly string[] BucketOrder =
    [
        EmployeeTaskBuckets.Overdue, EmployeeTaskBuckets.NotStarted,
        EmployeeTaskBuckets.InProgress, EmployeeTaskBuckets.Completed
    ];

    private static readonly string[] PriorityOrder =
    [
        WorkTaskPriorities.Critical, WorkTaskPriorities.High, WorkTaskPriorities.Medium, WorkTaskPriorities.Low
    ];

    public static List<EmployeeWorkTaskItem> Build(IEnumerable<EmployeeTaskPeriodRow> rows, DateOnly asOf) =>
        rows
            .Select(row => new EmployeeWorkTaskItem(
                row.TaskId, row.ShortId, row.Title,
                EmployeeTaskPeriodCalculator.Bucket(row, asOf),
                string.IsNullOrEmpty(row.Priority) ? WorkTaskPriorities.Medium : row.Priority,
                row.StatusName, row.StatusColor, row.DueDate,
                EmployeeTaskPeriodCalculator.DaysOverdue(row, asOf), row.IsCarriedOver,
                row.ProjectId, row.ProjectName, row.ObjectiveId, row.ObjectiveTitle))
            .OrderBy(i => Rank(BucketOrder, i.Bucket))
            .ThenBy(i => Rank(PriorityOrder, i.Priority))
            .ThenBy(i => i.DueDate is null)
            .ThenBy(i => i.DueDate)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Position in the given order; unknown values sort last.</summary>
    private static int Rank(string[] order, string value)
    {
        var index = Array.IndexOf(order, value);
        return index < 0 ? order.Length : index;
    }
}
