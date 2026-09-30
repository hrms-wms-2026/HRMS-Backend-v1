using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
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

    private readonly IEmployeeReadAccessGuard _guard;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IWorkTaskRepository _tasks;
    private readonly ICurrentUser _currentUser;

    public GetEmployeeWorkGraphQueryHandler(
        IEmployeeReadAccessGuard guard,
        IProjectMemberRepository members,
        IObjectiveRepository objectives,
        IProjectRepository projects,
        IWorkTaskRepository tasks,
        ICurrentUser currentUser)
    {
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
            nodes.Add(new WorkGraphNode(projectNodeId, WorkGraphNodeKinds.Project, project.Name, project.Identifier, ProjectId: project.Id));
            links.Add(new WorkGraphLink(employeeNodeId, projectNodeId, WorkGraphLinkKinds.WorksOn));
        }

        var moduleObjectives = objectivesById.Values
            .Where(o => !o.IsDefault && projectsById.ContainsKey(o.ProjectId))
            .OrderBy(o => o.Title)
            .ToList();

        foreach (var objective in moduleObjectives)
        {
            var moduleNodeId = $"module:{objective.Id}";
            var isOwner = objective.OwnerId == employeeId;
            var isMember = memberObjectiveIds.Contains(objective.Id);
            var role = isOwner ? WorkGraphModuleRoles.Owner
                : isMember ? WorkGraphModuleRoles.Member
                : WorkGraphModuleRoles.Contributor;

            nodes.Add(new WorkGraphNode(moduleNodeId, WorkGraphNodeKinds.Module, objective.Title,
                Role: role, ProjectId: objective.ProjectId, ObjectiveId: objective.Id));
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
                Status: task.Category, ProjectId: task.ProjectId, ObjectiveId: task.ObjectiveId, TaskId: task.Id));
            var parent = moduleIds.Contains(task.ObjectiveId) ? $"module:{task.ObjectiveId}" : $"project:{task.ProjectId}";
            links.Add(new WorkGraphLink(parent, taskNodeId, WorkGraphLinkKinds.HasTask));
            renderedTasks++;
        }

        var hidden = Math.Max(0, taskPage.TotalCount - renderedTasks);
        return Result<EmployeeWorkGraphResponse>.Success(new EmployeeWorkGraphResponse(nodes, links, hidden));
    }
}
