namespace ONEVO.Application.Features.WorkManagement.Leadership.DTOs;

/// <summary>Estimated-hours picture of a Module (its whole subtree) or a sum of Modules.
/// Completed + InProgress + NotStarted are task estimates by task state; UntaskedHours is the part
/// of AllocatedHours not yet given to any task, OverTaskedHours the amount tasks exceed it by.</summary>
public sealed record LedHourTotals(
    decimal AllocatedHours,
    decimal CompletedHours,
    decimal InProgressHours,
    decimal NotStartedHours,
    decimal UntaskedHours,
    decimal OverTaskedHours,
    int TaskCount,
    int UnestimatedTaskCount)
{
    public static readonly LedHourTotals Zero = new(0, 0, 0, 0, 0, 0, 0, 0);

    public decimal TaskedHours => CompletedHours + InProgressHours + NotStartedHours;
}

/// <param name="ParentObjectiveId">Null for a head Module (one the caller leads with no led ancestor);
/// otherwise the parent of this child Module.</param>
/// <param name="Depth">0 for a head Module, 1 for its children, and so on.</param>
/// <param name="AtRisk">The Module's subtree has a not-completed task past its due date.</param>
public sealed record LedModuleProgress(
    Guid ObjectiveId, string Title, bool IsRootModule, DateOnly EndDate, Guid? ParentObjectiveId, int Depth, bool AtRisk,
    LedHourTotals Hours);

/// <param name="Modules">Head Modules, each followed depth-first by its child Modules.</param>
public sealed record LedProjectProgress(
    Guid ProjectId, string ProjectName, string Identifier, LedHourTotals Hours, IReadOnlyList<LedModuleProgress> Modules);

public sealed record LedTaskAssignee(Guid EmployeeId, string DisplayName);

/// <param name="EstimatedHours">The task's estimate; null when it has none (it adds no hours).</param>
public sealed record LedOverdueTask(
    Guid TaskId, string ShortId, string Title, Guid ProjectId, Guid ObjectiveId, DateOnly DueDate, int DaysOverdue,
    IReadOnlyList<LedTaskAssignee> Assignees, decimal? EstimatedHours);

/// <summary>Every overdue top-level task of one project (not just the listed ones), summed.</summary>
public sealed record LedOverdueProject(Guid ProjectId, string ProjectName, decimal Hours, int TaskCount, int UnestimatedTaskCount);

/// <summary>Every overdue top-level task of one module, split like the module hour totals, so the Team Progress donut
/// can show overdue as its own slice out of In progress / Not started, under any project or module filter.</summary>
public sealed record LedOverdueModule(Guid ProjectId, Guid ObjectiveId, decimal InProgressHours, decimal NotStartedHours, int TaskCount);

/// <param name="ModulesAtRisk">Head Modules whose subtree has overdue work.</param>
/// <param name="OverdueHours">Estimated hours of every overdue top-level task (OverdueTotal of them).</param>
/// <param name="OverdueUnestimatedTaskCount">Overdue tasks with no estimate, so not in OverdueHours.</param>
/// <param name="OverdueByProject">OverdueHours by project, most hours first.</param>
/// <param name="OverdueByModule">OverdueHours by module, split into in progress / not started.</param>
public sealed record LedWorkProgressResponse(
    LedHourTotals Hours, IReadOnlyList<LedProjectProgress> Projects, IReadOnlyList<LedOverdueTask> OverdueTasks, int OverdueTotal,
    int ModulesAtRisk, decimal OverdueHours, int OverdueUnestimatedTaskCount, IReadOnlyList<LedOverdueProject> OverdueByProject,
    IReadOnlyList<LedOverdueModule> OverdueByModule);
