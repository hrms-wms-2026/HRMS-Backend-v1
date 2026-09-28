using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Notifications.Services;

/// <summary>Past-tense phrase used as {{actionLabel}} in the work_* templates.</summary>
public static class WorkActionLabels
{
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
        [WorkActionTypes.ProjectStatusTemplateChange] = "changed the task statuses of",
    };

    public static string For(string actionType) => Labels.GetValueOrDefault(actionType, "changed");
}
