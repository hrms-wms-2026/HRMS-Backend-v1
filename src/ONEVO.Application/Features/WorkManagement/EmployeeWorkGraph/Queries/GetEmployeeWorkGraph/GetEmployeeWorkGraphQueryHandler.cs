using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.Queries.GetEmployeeWorkGraph;

/// <summary>
/// Builds the employee-centred work graph (spec 2026-09-30 §4/§5): the employee, the projects
/// he belongs to, the non-default objectives ("modules") he owns / is a member of / has open
/// work in, and his not_started/active tasks (capped). Access is the shared employee read-access
/// guard, so a coverage viewer sees his whole Work Management footprint (deliberate HR decision).
/// </summary>
public class GetEmployeeWorkGraphQueryHandler : IRequestHandler<GetEmployeeWorkGraphQuery, Result<EmployeeWorkGraphResponse>>
{
    public const int MaxTaskNodes = 60;
    /// <summary>Members listed per module (with avatars); the full count is sent separately.</summary>
    public const int MaxModuleMembers = 8;

    private readonly IEmployeeReadAccessGuard _guard;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IWorkTaskRepository _tasks;
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IDateTimeProvider _clock;
    private readonly IEntityAssetRepository _entityAssets;

    public GetEmployeeWorkGraphQueryHandler(
        IEmployeeReadAccessGuard guard,
        IProjectMemberRepository members,
        IObjectiveRepository objectives,
        IProjectRepository projects,
        IWorkTaskRepository tasks,
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IDateTimeProvider clock,
        IEntityAssetRepository entityAssets)
    {
        _entityAssets = entityAssets;
        _identity = identity;
        _clock = clock;
        _guard = guard;
        _members = members;
        _objectives = objectives;
        _projects = projects;
        _tasks = tasks;
        _currentUser = currentUser;
    }

    public async Task<Result<EmployeeWorkGraphResponse>> Handle(GetEmployeeWorkGraphQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var employeeId = request.EmployeeId;

        var access = await _guard.EnsureCanRead(tenantId, employeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeWorkGraphResponse>.Failure(access.Error!, access.StatusCode ?? 400);
        var employee = access.Value!;

        var memberships = await _members.ListActiveForEmployeeAsync(tenantId, employeeId, ct);
        var owned = await _objectives.ListActiveOwnedByEmployeeAsync(tenantId, employeeId, ct);
        var taskPage = await _tasks.ListOpenAssignedToEmployeeAsync(tenantId, employeeId, MaxTaskNodes, ct);

        var objectiveIds = memberships.Select(m => m.ObjectiveId)
            .Concat(owned.Select(o => o.Id))
            .Concat(taskPage.Items.Select(t => t.ObjectiveId))
            .ToHashSet();
        var objectivesById = (await _objectives.GetByIdsForTenantAsync(tenantId, objectiveIds, ct))
            .Where(o => o.IsActive)
            .ToDictionary(o => o.Id);

        var projectIds = memberships.Select(m => m.ProjectId)
            .Concat(owned.Select(o => o.ProjectId))
            .Concat(taskPage.Items.Select(t => t.ProjectId))
            .Concat(objectivesById.Values.Select(o => o.ProjectId))
            .ToHashSet();
        var projectsById = (await _projects.GetActiveByIdsForTenantAsync(tenantId, projectIds, ct))
            .ToDictionary(p => p.Id);

        var today = _clock.Today;
        var moduleObjectiveList = objectivesById.Values
            .Where(o => !o.IsDefault && projectsById.ContainsKey(o.ProjectId))
            .ToList();
        var moduleObjectiveIds = moduleObjectiveList.Select(o => o.Id).ToList();
        var moduleCounts = await _tasks.CountByObjectivesAsync(tenantId, moduleObjectiveIds, today, ct);
        var projectCounts = await _tasks.CountByProjectsAsync(tenantId, projectsById.Keys.ToList(), today, ct);
        var moduleMembers = await _members.ListActiveMemberEmployeeIdsByObjectivesAsync(tenantId, moduleObjectiveIds, ct);
        var projectLogos = projectsById.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
                tenantId, EntityAssetOwnerTypes.Project, projectsById.Keys.ToList(), UploadPurposeCatalog.ProjectCover, ct);
        var peopleIds = moduleObjectiveList.Select(o => o.OwnerId)
            .Concat(moduleMembers.Values.SelectMany(ids => ids.Take(MaxModuleMembers)))
            .Distinct()
            .ToList();
        var people = peopleIds.Count == 0
            ? new Dictionary<Guid, EmployeeIdentityDto>()
            : await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, peopleIds, ct);
        WorkGraphPerson? Person(Guid id) =>
            people.TryGetValue(id, out var p) ? new WorkGraphPerson(id, p.Name, p.AvatarFileId) : null;
        static WorkGraphStats StatsOf(WorkTaskCounts? c, decimal allocated, decimal completed) =>
            new(c?.Total ?? 0, c?.NotStarted ?? 0, c?.Active ?? 0, c?.Done ?? 0, c?.Overdue ?? 0, allocated, completed);

        var memberObjectiveIds = memberships.Select(m => m.ObjectiveId).ToHashSet();
        var employeeNodeId = $"employee:{employeeId}";
        var nodes = new List<WorkGraphNode>
        {
            new(employeeNodeId, WorkGraphNodeKinds.Employee, employee.FullName, employee.PositionName)
        };
        var links = new List<WorkGraphLink>();

        foreach (var project in projectsById.Values.OrderBy(p => p.Name))
        {
            var projectNodeId = $"project:{project.Id}";
            nodes.Add(new WorkGraphNode(projectNodeId, WorkGraphNodeKinds.Project, project.Name, project.Identifier, ProjectId: project.Id,
                Stats: StatsOf(projectCounts.GetValueOrDefault(project.Id), project.AllocatedHours, project.CompletedHours),
                LogoFileId: projectLogos.TryGetValue(project.Id, out var logo) ? logo : null));
            links.Add(new WorkGraphLink(employeeNodeId, projectNodeId, WorkGraphLinkKinds.WorksOn));
        }

        var moduleObjectives = moduleObjectiveList.OrderBy(o => o.Title).ToList();

        foreach (var objective in moduleObjectives)
        {
            var moduleNodeId = $"module:{objective.Id}";
            var isOwner = objective.OwnerId == employeeId;
            var isMember = memberObjectiveIds.Contains(objective.Id);
            var role = isOwner ? WorkGraphModuleRoles.Owner
                : isMember ? WorkGraphModuleRoles.Member
                : WorkGraphModuleRoles.Contributor;

            var memberIds = moduleMembers.GetValueOrDefault(objective.Id) ?? Array.Empty<Guid>();
            var details = new WorkGraphModuleDetails(
                Person(objective.OwnerId), objective.StartDate, objective.EndDate,
                memberIds.Take(MaxModuleMembers).Select(Person).OfType<WorkGraphPerson>().ToList(),
                memberIds.Count);
            nodes.Add(new WorkGraphNode(moduleNodeId, WorkGraphNodeKinds.Module, objective.Title,
                Role: role, ProjectId: objective.ProjectId, ObjectiveId: objective.Id,
                Stats: StatsOf(moduleCounts.GetValueOrDefault(objective.Id), objective.AllocatedHours, objective.CompletedHours),
                Module: details));
            links.Add(new WorkGraphLink($"project:{objective.ProjectId}", moduleNodeId, WorkGraphLinkKinds.Contains));
            if (isOwner)
                links.Add(new WorkGraphLink(employeeNodeId, moduleNodeId, WorkGraphLinkKinds.Owns));
            else if (isMember)
                links.Add(new WorkGraphLink(employeeNodeId, moduleNodeId, WorkGraphLinkKinds.MemberOf));
        }

        var moduleIds = moduleObjectives.Select(o => o.Id).ToHashSet();
        var renderedTasks = 0;
        foreach (var task in taskPage.Items)
        {
            if (!projectsById.ContainsKey(task.ProjectId))
                continue;

            var taskNodeId = $"task:{task.Id}";
            nodes.Add(new WorkGraphNode(taskNodeId, WorkGraphNodeKinds.Task, task.Title, task.ShortId,
                Status: task.Category, ProjectId: task.ProjectId, ObjectiveId: task.ObjectiveId, TaskId: task.Id,
                Task: new WorkGraphTaskDetails(task.DueDate, task.StatusName, task.Priority,
                    task.DueDate is { } due && due < today)));
            var parent = moduleIds.Contains(task.ObjectiveId) ? $"module:{task.ObjectiveId}" : $"project:{task.ProjectId}";
            links.Add(new WorkGraphLink(parent, taskNodeId, WorkGraphLinkKinds.HasTask));
            renderedTasks++;
        }

        var hidden = Math.Max(0, taskPage.TotalCount - renderedTasks);
        return Result<EmployeeWorkGraphResponse>.Success(new EmployeeWorkGraphResponse(nodes, links, hidden));
    }
}
