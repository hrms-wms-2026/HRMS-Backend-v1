using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyProjectMilestones;

public class GetMyProjectMilestonesQueryHandler : IRequestHandler<GetMyProjectMilestonesQuery, Result<IReadOnlyList<MyProjectMilestoneResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;

    public GetMyProjectMilestonesQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectMemberRepository members,
        IObjectiveRepository objectives)
    {
        _currentUser = currentUser;
        _identity = identity;
        _members = members;
        _objectives = objectives;
    }

    public async Task<Result<IReadOnlyList<MyProjectMilestoneResponse>>> Handle(GetMyProjectMilestonesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Forbidden("No employee record for the current user.");

        var memberships = await _members.ListForEmployeeInProjectAsync(tenantId, request.ProjectId, callerEmployeeId.Value, ct);
        var allObjectives = await _objectives.GetAllByProjectIdAsync(tenantId, request.ProjectId, ct);

        var objectivesById = allObjectives.ToDictionary(o => o.Id);
        var membershipsByObjectiveId = memberships.ToDictionary(m => m.ObjectiveId);
        var activeMembershipObjectiveIds = memberships.Where(m => m.IsActive).Select(m => m.ObjectiveId).ToHashSet();

        // Rights cascade down from any ancestor's owner or active member (design:
        // 2026-08-21-work-management-cascading-objective-ownership-design.md), mirroring
        // IMilestoneMembershipCoordinator.IsEffectiveManagerAsync. Walked in-memory here (rather
        // than calling that DB-hitting helper per objective) since allObjectives already holds the
        // whole project's tree.
        bool IsEffectiveManager(Objective objective)
        {
            Objective? cursor = objective;
            while (cursor is not null)
            {
                if (cursor.OwnerId == callerEmployeeId.Value || activeMembershipObjectiveIds.Contains(cursor.Id))
                    return true;
                cursor = cursor.ParentObjectiveId is { } parentId ? objectivesById.GetValueOrDefault(parentId) : null;
            }
            return false;
        }

        // Every objective the caller can act on: has a direct project_members row (any status - the
        // frontend filters by membershipIsActive as needed) OR is reachable via the ownership cascade.
        var relevant = allObjectives
            .Select(o => (Objective: o, IsEffectiveManager: IsEffectiveManager(o)))
            .Where(r => r.IsEffectiveManager || membershipsByObjectiveId.ContainsKey(r.Objective.Id))
            .ToList();

        if (relevant.Count == 0)
            return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Success(Array.Empty<MyProjectMilestoneResponse>());

        var nameLookupIds = new HashSet<Guid>();
        foreach (var (objective, _) in relevant)
        {
            nameLookupIds.Add(objective.OwnerId);
            if (objective.ReportingManagerId.HasValue)
                nameLookupIds.Add(objective.ReportingManagerId.Value);
        }

        var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, nameLookupIds.ToList(), ct);

        var items = new List<MyProjectMilestoneResponse>();
        foreach (var (objective, isEffectiveManager) in relevant)
        {
            namesByEmployeeId.TryGetValue(objective.OwnerId, out var ownerName);
            string? reportingManagerName = null;
            if (objective.ReportingManagerId.HasValue)
                namesByEmployeeId.TryGetValue(objective.ReportingManagerId.Value, out reportingManagerName);

            // Cascade-only rows (no direct membership row) have live effective access by
            // definition, so they report as an active membership with no removal date.
            var membership = membershipsByObjectiveId.GetValueOrDefault(objective.Id);
            var membershipIsActive = membership?.IsActive ?? true;
            var membershipRemovedAt = membership?.RemovedAt;

            items.Add(new MyProjectMilestoneResponse(
                objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title,
                objective.OwnerId, ownerName, objective.ReportingManagerId, reportingManagerName,
                objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours,
                objective.IsActive, objective.IsAchieved, objective.AchievedAt,
                membershipIsActive, membershipRemovedAt, isEffectiveManager));
        }

        return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Success(items);
    }
}
