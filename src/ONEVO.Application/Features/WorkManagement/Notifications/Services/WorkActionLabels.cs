using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Notifications.Services;

/// <summary>Past-tense phrase used as {{actionLabel}} in the work_* templates.</summary>
public static class WorkActionLabels
{
    public const string MonitorPrefix = "monitor.";

    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [WorkActionTypes.TaskCreate] = "created the task",
        [WorkActionTypes.TaskEdit] = "edited the task",
        [WorkActionTypes.TaskDelete] = "deleted the task",
        [WorkActionTypes.TaskStatusChange] = "changed the status of the task",
        [WorkActionTypes.ModuleEdit] = "edited the module",
        [WorkActionTypes.ModuleDelete] = "deleted the module",
        [WorkActionTypes.ModuleTransfer] = "transferred the module",
        [WorkActionTypes.ModuleAchieve] = "achieved the module",
        [WorkActionTypes.ModuleUnachieve] = "reopened the module",
        [WorkActionTypes.ModuleAllocationExtend] = "extended the allocation of the module",
        [WorkActionTypes.SprintCreate] = "created the sprint",
        [WorkActionTypes.SprintEdit] = "edited the sprint",
        [WorkActionTypes.SprintDelete] = "deleted the sprint",
        [WorkActionTypes.SprintStart] = "started the sprint",
        [WorkActionTypes.SprintComplete] = "completed the sprint",
        [WorkActionTypes.SprintAchieve] = "achieved the sprint",
        [WorkActionTypes.ProjectStatusTemplateChange] = "changed the task statuses of",

        // Project monitor alerts (ActionType = MonitorPrefix + rule code). Used as "{{actionLabel}}: \"{{targetTitle}}\"".
        [MonitorPrefix + "module_over_capacity"] = "Module over capacity",
        [MonitorPrefix + "module_capacity_shortfall"] = "Module cannot finish in time",
        [MonitorPrefix + "employee_deadline_overload"] = "Assignee overloaded before the deadline",
        [MonitorPrefix + "task_clocked_over_estimate"] = "Clocked hours exceed the estimate",
        [MonitorPrefix + "module_clocked_over_allocated"] = "Clocked hours exceed the allocation",
        [MonitorPrefix + "sprint_overdue"] = "Sprint past its end date",
        [MonitorPrefix + "task_overdue"] = "Task past its due date",
        [MonitorPrefix + "module_overdue"] = "Module past its end date",
    };

    public static string For(string actionType) => Labels.GetValueOrDefault(actionType, "changed");
}
