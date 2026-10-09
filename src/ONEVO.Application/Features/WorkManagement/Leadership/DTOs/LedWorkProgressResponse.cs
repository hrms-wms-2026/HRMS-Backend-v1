namespace ONEVO.Application.Features.WorkManagement.Leadership.DTOs;

public sealed record LedWorkTotals(int Total, int Completed, int InProgress, int NotStarted, int Overdue)
{
    public static readonly LedWorkTotals Zero = new(0, 0, 0, 0, 0);
}

public sealed record LedModuleProgress(Guid ObjectiveId, string Title, bool IsRootModule, DateOnly EndDate, LedWorkTotals Totals);

public sealed record LedProjectProgress(
    Guid ProjectId, string ProjectName, string Identifier, LedWorkTotals Totals, IReadOnlyList<LedModuleProgress> Modules);

public sealed record LedTaskAssignee(Guid EmployeeId, string DisplayName);

public sealed record LedOverdueTask(
    Guid TaskId, string ShortId, string Title, Guid ProjectId, Guid ObjectiveId, DateOnly DueDate, int DaysOverdue,
    IReadOnlyList<LedTaskAssignee> Assignees);

public sealed record LedWorkProgressResponse(
    LedWorkTotals Totals, IReadOnlyList<LedProjectProgress> Projects, IReadOnlyList<LedOverdueTask> OverdueTasks, int OverdueTotal);
