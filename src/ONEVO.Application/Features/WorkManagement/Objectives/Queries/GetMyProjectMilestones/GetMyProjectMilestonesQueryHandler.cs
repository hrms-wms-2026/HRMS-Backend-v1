using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

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
        if (memberships.Count == 0)
            return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Success(Array.Empty<MyProjectMilestoneResponse>());

        var allObjectives = await _objectives.GetAllByProjectIdAsync(tenantId, request.ProjectId, ct);
        var objectivesById = allObjectives.ToDictionary(o => o.Id);

        var nameLookupIds = new HashSet<Guid>();
        foreach (var membership in memberships)
        {
            if (!objectivesById.TryGetValue(membership.ObjectiveId, out var objective))
                continue;

            nameLookupIds.Add(objective.OwnerId);
            if (objective.ReportingManagerId.HasValue)
                nameLookupIds.Add(objective.ReportingManagerId.Value);
        }

        var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, nameLookupIds.ToList(), ct);

        var items = new List<MyProjectMilestoneResponse>();
        foreach (var membership in memberships)
        {
            if (!objectivesById.TryGetValue(membership.ObjectiveId, out var objective))
                continue;

            namesByEmployeeId.TryGetValue(objective.OwnerId, out var ownerName);
            string? reportingManagerName = null;
            if (objective.ReportingManagerId.HasValue)
                namesByEmployeeId.TryGetValue(objective.ReportingManagerId.Value, out reportingManagerName);

            items.Add(new MyProjectMilestoneResponse(
                objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title,
                objective.OwnerId, ownerName, objective.ReportingManagerId, reportingManagerName,
                objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours,
                objective.IsActive, objective.IsAchieved, objective.AchievedAt,
                membership.IsActive, membership.RemovedAt, objective.OwnerId == callerEmployeeId.Value));
        }

        return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Success(items);
    }
}
