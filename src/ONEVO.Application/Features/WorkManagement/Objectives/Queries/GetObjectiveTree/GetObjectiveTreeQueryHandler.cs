using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;

public class GetObjectiveTreeQueryHandler : IRequestHandler<GetObjectiveTreeQuery, Result<IReadOnlyList<ObjectiveTreeItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;

    public GetObjectiveTreeQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        IProjectMemberRepository members, IObjectiveRepository objectives)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
        _objectives = objectives;
    }

    public async Task<Result<IReadOnlyList<ObjectiveTreeItemResponse>>> Handle(GetObjectiveTreeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.NotFound("Project not found.");

        var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
        if (!isMember)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("You do not have access to this project's milestone tree.");

        var allObjectives = await _objectives.GetTreeByProjectIdAsync(tenantId, project.Id, ct);
        var ownedObjectiveIds = (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct)).ToHashSet();

        var byId = allObjectives.ToDictionary(o => o.Id);
        var childrenByParent = allObjectives
            .Where(o => o.ParentObjectiveId is not null)
            .GroupBy(o => o.ParentObjectiveId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var ownerReachable = new HashSet<Guid>();
        foreach (var ownedId in ownedObjectiveIds)
        {
            if (!byId.ContainsKey(ownedId))
                continue;

            var queue = new Queue<Guid>();
            queue.Enqueue(ownedId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (ownerReachable.Add(current) && childrenByParent.TryGetValue(current, out var children))
                {
                    foreach (var child in children)
                        queue.Enqueue(child.Id);
                }
            }
        }

        var defaultObjective = allObjectives.FirstOrDefault(o => o.IsDefault);
        var hasDirectMembership = defaultObjective is not null
            && await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, project.Id, callerEmployeeId.Value, new[] { defaultObjective.Id }, ct);

        if (hasDirectMembership)
        {
            var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
                tenantId, allObjectives.Select(o => o.OwnerId).Distinct().ToList(), ct);
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Success(
                allObjectives.Select(o => ObjectiveMapper.ToTreeItem(o, ownedObjectiveIds.Contains(o.Id) || ownerReachable.Contains(o.Id), namesByEmployeeId.GetValueOrDefault(o.OwnerId))).ToList());
        }

        var reachable = new HashSet<Guid>();
        foreach (var ownedId in ownedObjectiveIds)
        {
            if (!byId.TryGetValue(ownedId, out var owned))
                continue;

            reachable.Add(owned.Id);

            var cursor = owned;
            while (cursor.ParentObjectiveId is not null && byId.TryGetValue(cursor.ParentObjectiveId.Value, out var parent))
            {
                reachable.Add(parent.Id);
                cursor = parent;
            }

            var queue = new Queue<Guid>();
            queue.Enqueue(owned.Id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!childrenByParent.TryGetValue(current, out var children))
                    continue;

                foreach (var child in children)
                {
                    if (reachable.Add(child.Id))
                        queue.Enqueue(child.Id);
                }
            }
        }

        var scopedObjectives = allObjectives.Where(o => reachable.Contains(o.Id)).ToList();
        var scopedNamesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, scopedObjectives.Select(o => o.OwnerId).Distinct().ToList(), ct);
        var scoped = scopedObjectives
            .Select(o => ObjectiveMapper.ToTreeItem(o, ownedObjectiveIds.Contains(o.Id) || ownerReachable.Contains(o.Id), scopedNamesByEmployeeId.GetValueOrDefault(o.OwnerId)))
            .ToList();
        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Success(scoped);
    }
}
