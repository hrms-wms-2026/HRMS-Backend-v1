using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

/// <summary>The audience query kept from the retired SprintAccessService.</summary>
public sealed class SprintAudienceResolver : ISprintAudienceResolver
{
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectMemberRepository _members;

    public SprintAudienceResolver(IWorkTaskRepository tasks, IProjectMemberRepository members)
    {
        _tasks = tasks;
        _members = members;
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
