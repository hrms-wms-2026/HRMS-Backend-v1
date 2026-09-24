using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public class SprintTaskAssignmentService : ISprintTaskAssignmentService
{
    private readonly IWorkTaskRepository _tasks;
    private readonly ISprintRepository _sprints;
    private readonly IMilestoneMembershipCoordinator _membership;

    public SprintTaskAssignmentService(IWorkTaskRepository tasks, ISprintRepository sprints, IMilestoneMembershipCoordinator membership)
    {
        _tasks = tasks;
        _sprints = sprints;
        _membership = membership;
    }

    public async Task<Result<SprintTaskChangeSet>> PrepareAsync(
        Guid tenantId, Sprint sprint, IReadOnlyCollection<Guid> addTaskIds, IReadOnlyCollection<Guid> removeTaskIds,
        Guid callerEmployeeId, CancellationToken ct = default)
    {
        var addIds = addTaskIds.Distinct().ToList();
        var removeIds = removeTaskIds.Distinct().Where(id => !addIds.Contains(id)).ToList();
        if (addIds.Count == 0 && removeIds.Count == 0)
            return Result<SprintTaskChangeSet>.Success(SprintTaskChangeSet.Empty);

        if (sprint.Status is not (SprintStatuses.Draft or SprintStatuses.Active))
            return Result<SprintTaskChangeSet>.Conflict("Tasks can only be added to or removed from a Draft or Active sprint.");

        var ownership = new Dictionary<Guid, bool>();
        async Task<bool> OwnsAsync(Guid objectiveId)
        {
            if (!ownership.TryGetValue(objectiveId, out var owns))
            {
                owns = await _membership.IsEffectiveOwnerAsync(tenantId, objectiveId, callerEmployeeId, ct);
                ownership[objectiveId] = owns;
            }
            return owns;
        }

        var toAdd = new List<WorkTask>();
        foreach (var id in addIds)
        {
            var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, id, ct);
            if (task is null || task.ProjectId != sprint.ProjectId)
                return Result<SprintTaskChangeSet>.NotFound($"Task {id} not found in this project.");
            if (task.SprintId == sprint.Id) continue;
            if (task.SprintId is not null)
            {
                var current = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
                if (current is not null && current.Status == SprintStatuses.Achieved)
                    return Result<SprintTaskChangeSet>.Forbidden($"Task {task.ShortId} is in an achieved sprint and is frozen.");
            }
            if (!await OwnsAsync(task.ObjectiveId))
                return Result<SprintTaskChangeSet>.Forbidden("You can only add tasks from modules you own.");
            toAdd.Add(task);
        }

        var toRemove = new List<WorkTask>();
        foreach (var id in removeIds)
        {
            var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, id, ct);
            if (task is null || task.SprintId != sprint.Id) continue;
            if (!await OwnsAsync(task.ObjectiveId))
                return Result<SprintTaskChangeSet>.Forbidden("You can only remove tasks from modules you own.");
            toRemove.Add(task);
        }

        return Result<SprintTaskChangeSet>.Success(new SprintTaskChangeSet(toAdd, toRemove));
    }

    public void Apply(SprintTaskChangeSet changes, Guid sprintId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var task in changes.ToAdd)
        {
            task.SprintId = sprintId;
            task.UpdatedAt = now;
            _tasks.Update(task);
        }
        foreach (var task in changes.ToRemove)
        {
            task.SprintId = null;
            task.UpdatedAt = now;
            _tasks.Update(task);
        }
    }
}
