using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectWorkGraph.Queries.GetProjectWorkGraph;

/// <summary>
/// Builds the project-centred work graph: every active objective of the project as a module node,
/// parent-to-child "contains" links, and the project's open top-level tasks hanging off their module
/// by "has_task" links (the kind the explorer groups task cards by). Access mirrors GetObjectiveTreeQueryHandler exactly - every active project member sees
/// the full module tree, so this query invents no visibility rule of its own.
/// Modules carry no Role: it is viewer-relative ("do I own this") and says nothing useful in a
/// project-rooted picture, where the owner is on the node's own label and in the detail panel.
/// </summary>
public class GetProjectWorkGraphQueryHandler : IRequestHandler<GetProjectWorkGraphQuery, Result<ProjectWorkGraphResponse>>
{
    public const int MaxTaskNodes = 60;
    /// <summary>Members listed per module (with avatars); the full count is sent separately.</summary>
    public const int MaxModuleMembers = 8;

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;
    private readonly IDateTimeProvider _clock;
    private readonly ITaskAccessResolver _taskAccess;
    private readonly ITaskStatusRepository _statuses;

    public GetProjectWorkGraphQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        IProjectMemberRepository members, IObjectiveRepository objectives, IWorkTaskRepository tasks,
        IDateTimeProvider clock, ITaskAccessResolver taskAccess, ITaskStatusRepository statuses)
    {
        _taskAccess = taskAccess;
        _statuses = statuses;
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
        _objectives = objectives;
        _tasks = tasks;
        _clock = clock;
    }

    public async Task<Result<ProjectWorkGraphResponse>> Handle(GetProjectWorkGraphQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectWorkGraphResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<ProjectWorkGraphResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<ProjectWorkGraphResponse>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<ProjectWorkGraphResponse>.NotFound("Project not found.");

        var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
        if (!isMember)
            return Result<ProjectWorkGraphResponse>.Forbidden("You do not have access to this project's work network.");

        var objectives = (await _objectives.GetTreeByProjectIdAsync(tenantId, project.Id, ct))
            .Where(o => o.IsActive)
            .OrderBy(o => o.Title)
            .ToList();
        var objectiveIds = objectives.Select(o => o.Id).ToList();

        var today = _clock.Today;
        var counts = await _tasks.CountByObjectivesAsync(tenantId, objectiveIds, today, ct);
        var moduleMembers = await _members.ListActiveMemberEmployeeIdsByObjectivesAsync(tenantId, objectiveIds, ct);

        var peopleIds = objectives.Select(o => o.OwnerId)
            .Concat(moduleMembers.Values.SelectMany(ids => ids.Take(MaxModuleMembers)))
            .Distinct()
            .ToList();
        var people = peopleIds.Count == 0
            ? new Dictionary<Guid, EmployeeIdentityDto>()
            : await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, peopleIds, ct);
        WorkGraphPerson? Person(Guid id) =>
            people.TryGetValue(id, out var p) ? new WorkGraphPerson(id, p.Name, p.AvatarFileId) : null;

        var nodes = new List<WorkGraphNode>();
        var links = new List<WorkGraphLink>();
        var presentIds = objectives.Select(o => o.Id).ToHashSet();

        foreach (var objective in objectives)
        {
            var memberIds = moduleMembers.GetValueOrDefault(objective.Id) ?? Array.Empty<Guid>();
            var c = counts.GetValueOrDefault(objective.Id);
            nodes.Add(new WorkGraphNode(
                $"module:{objective.Id}", WorkGraphNodeKinds.Module, objective.Title,
                ProjectId: objective.ProjectId, ObjectiveId: objective.Id,
                Stats: new WorkGraphStats(
                    c?.Total ?? 0, c?.NotStarted ?? 0, c?.Active ?? 0, c?.Done ?? 0, c?.Overdue ?? 0,
                    objective.AllocatedHours, objective.CompletedHours),
                Module: new WorkGraphModuleDetails(
                    Person(objective.OwnerId), objective.StartDate, objective.EndDate,
                    memberIds.Take(MaxModuleMembers).Select(Person).OfType<WorkGraphPerson>().ToList(),
                    memberIds.Count)));

            if (objective.ParentObjectiveId is { } parentId && presentIds.Contains(parentId))
                links.Add(new WorkGraphLink($"module:{parentId}", $"module:{objective.Id}", WorkGraphLinkKinds.Contains));
        }

        var (taskNodes, taskLinks, hidden) = await BuildTaskNodesAsync(
            tenantId, project.Id, callerEmployeeId.Value, presentIds, today, ct);
        nodes.AddRange(taskNodes);
        links.AddRange(taskLinks);

        return Result<ProjectWorkGraphResponse>.Success(new ProjectWorkGraphResponse(nodes, links, hidden));
    }

    /// <summary>
    /// Open (not started / active) top-level tasks of the drawn modules. Subtasks are left out, as
    /// they are from the tree's counts and progress. Visibility is applied BEFORE the cap, so a
    /// private task the caller may not see is simply absent - never drawn, never counted as hidden.
    /// </summary>
    private async Task<(List<WorkGraphNode> Nodes, List<WorkGraphLink> Links, int Hidden)> BuildTaskNodesAsync(
        Guid tenantId, Guid projectId, Guid callerEmployeeId, IReadOnlySet<Guid> moduleIds, DateOnly today,
        CancellationToken ct)
    {
        var topLevel = (await _tasks.GetByProjectAsync(tenantId, projectId, ct))
            .Where(t => t.ParentTaskId is null && moduleIds.Contains(t.ObjectiveId))
            .ToList();
        if (topLevel.Count == 0)
            return (new List<WorkGraphNode>(), new List<WorkGraphLink>(), 0);

        var statuses = (await _statuses.GetByIdsForTenantAsync(
                tenantId, topLevel.Select(t => t.StatusId).Distinct().ToList(), ct))
            .ToDictionary(s => s.Id);
        var open = topLevel
            .Where(t => statuses.TryGetValue(t.StatusId, out var s)
                        && (s.Category == TaskStatusCategories.NotStarted || s.Category == TaskStatusCategories.Active))
            .ToList();

        var viewable = await _taskAccess.FilterViewableTasksAsync(tenantId, _currentUser.UserId, callerEmployeeId, open, ct);
        var drawn = viewable
            .OrderBy(t => statuses[t.StatusId].Category)
            .ThenBy(t => t.ShortId)
            .Take(MaxTaskNodes)
            .ToList();

        var nodes = new List<WorkGraphNode>(drawn.Count);
        var links = new List<WorkGraphLink>(drawn.Count);
        foreach (var task in drawn)
        {
            var status = statuses[task.StatusId];
            var taskNodeId = $"task:{task.Id}";
            nodes.Add(new WorkGraphNode(taskNodeId, WorkGraphNodeKinds.Task, task.Title, task.ShortId,
                Status: status.Category, ProjectId: task.ProjectId, ObjectiveId: task.ObjectiveId, TaskId: task.Id,
                Task: new WorkGraphTaskDetails(task.DueDate, status.Name, task.Priority,
                    task.DueDate is { } due && due < today)));
            links.Add(new WorkGraphLink($"module:{task.ObjectiveId}", taskNodeId, WorkGraphLinkKinds.HasTask));
        }

        return (nodes, links, viewable.Count - drawn.Count);
    }
}
