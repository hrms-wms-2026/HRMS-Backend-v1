using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>Who hears about a direct task edit or delete: the creator-position holder, the assignees and the Module owner.</summary>
public static class TaskChangeRecipients
{
    public static IReadOnlyCollection<Guid> For(
        ProjectModuleTree tree, WorkTask task, Objective objective, IEnumerable<Guid> assigneeEmployeeIds)
    {
        var recipients = new HashSet<Guid>(assigneeEmployeeIds) { objective.OwnerId };
        if (tree.Get(task.CreatorPositionObjectiveId ?? task.ObjectiveId)?.OwnerId is { } positionHolder)
            recipients.Add(positionHolder);
        return recipients;
    }
}
