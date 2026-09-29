using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public class SprintAccessService : ISprintAccessService
{
    private readonly IWorkTaskRepository _tasks;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectMemberRepository _members;

    public SprintAccessService(IWorkTaskRepository tasks, IMilestoneMembershipCoordinator membership, IProjectMemberRepository members)
    {
        _tasks = tasks;
        _membership = membership;
        _members = members;
    }

    public async Task<bool> CanManageAsync(Guid tenantId, Sprint sprint, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default)
    {
        if (sprint.CreatedById == callerUserId) return true;

        var tasks = await _tasks.GetBySprintIdAsync(tenantId, sprint.Id, ct);
        foreach (var objectiveId in tasks.Select(t => t.ObjectiveId).Distinct())
        {
            if (await _membership.IsEffectiveOwnerAsync(tenantId, objectiveId, callerEmployeeId, ct))
                return true;
        }
        return false;
    }

    public async Task<IReadOnlySet<Guid>> GetManageableSprintIdsAsync(
        Guid tenantId, Guid projectId, IReadOnlyList<Sprint> sprints, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default)
    {
        var result = new HashSet<Guid>(sprints.Where(s => s.CreatedById == callerUserId).Select(s => s.Id));
        var pending = sprints.Where(s => !result.Contains(s.Id)).Select(s => s.Id).ToHashSet();
        if (pending.Count == 0) return result;

        var projectTasks = await _tasks.GetByProjectAsync(tenantId, projectId, ct);
        var ownership = new Dictionary<Guid, bool>();
        foreach (var group in projectTasks.Where(t => t.SprintId is not null && pending.Contains(t.SprintId.Value)).GroupBy(t => t.SprintId!.Value))
        {
            foreach (var objectiveId in group.Select(t => t.ObjectiveId).Distinct())
            {
                if (!ownership.TryGetValue(objectiveId, out var owns))
                {
                    owns = await _membership.IsEffectiveOwnerAsync(tenantId, objectiveId, callerEmployeeId, ct);
                    ownership[objectiveId] = owns;
                }
                if (owns) { result.Add(group.Key); break; }
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<Guid>> GetAudienceEmployeeIdsAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default)
    {
        var tasks = await _tasks.GetBySprintIdAsync(tenantId, sprintId, ct);
        var audience = new HashSet<Guid>();
        foreach (var objectiveId in tasks.Select(t => t.ObjectiveId).Distinct())
        {
            var members = await _members.ListActiveForObjectiveAsync(tenantId, objectiveId, ct);
            foreach (var member in members) audience.Add(member.EmployeeId);
        }
        return audience.ToList();
    }
}
