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

public sealed record LedOverdueTask(
    Guid TaskId, string ShortId, string Title, Guid ProjectId, Guid ObjectiveId, DateOnly DueDate, int DaysOverdue,
    IReadOnlyList<LedTaskAssignee> Assignees);

/// <param name="ModulesAtRisk">Head Modules whose subtree has overdue work.</param>
public sealed record LedWorkProgressResponse(
    LedHourTotals Hours, IReadOnlyList<LedProjectProgress> Projects, IReadOnlyList<LedOverdueTask> OverdueTasks, int OverdueTotal,
    int ModulesAtRisk);
