using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetProjectCalendar;

public sealed class GetProjectCalendarQueryHandler
    : IRequestHandler<GetProjectCalendarQuery, Result<IReadOnlyList<ProjectCalendarItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly ICalendarEventRepository _calendarEvents;

    public GetProjectCalendarQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectMemberRepository members,
        IObjectiveRepository objectives,
        ICalendarEventRepository calendarEvents)
    {
        _currentUser = currentUser;
        _identity = identity;
        _members = members;
        _objectives = objectives;
        _calendarEvents = calendarEvents;
    }

    public async Task<Result<IReadOnlyList<ProjectCalendarItemResponse>>> Handle(
        GetProjectCalendarQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ProjectCalendarItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ProjectCalendarItemResponse>>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<ProjectCalendarItemResponse>>.Forbidden("No employee record for the current user.");

        var memberships = await _members.ListForEmployeeInProjectAsync(tenantId, request.ProjectId, callerEmployeeId.Value, ct);
        var objectives = await _objectives.GetAllByProjectIdAsync(tenantId, request.ProjectId, ct);
        if (objectives.Count == 0)
            return Result<IReadOnlyList<ProjectCalendarItemResponse>>.Success(Array.Empty<ProjectCalendarItemResponse>());

        var objectivesById = objectives.ToDictionary(o => o.Id);
        var activeMembershipObjectiveIds = memberships.Where(m => m.IsActive).Select(m => m.ObjectiveId).ToHashSet();

        bool IsEffectiveManager(Objective objective)
        {
            Objective? cursor = objective;
            while (cursor is not null)
            {
                if (cursor.OwnerId == callerEmployeeId.Value || activeMembershipObjectiveIds.Contains(cursor.Id))
                    return true;

                cursor = cursor.ParentObjectiveId is { } parentId
                    ? objectivesById.GetValueOrDefault(parentId)
                    : null;
            }

            return false;
        }

        var activeEventMemberships = await _calendarEvents.ListActiveMembershipsForProjectAsync(tenantId, request.ProjectId, ct);
        var eventByObjectiveId = activeEventMemberships
            .GroupBy(m => m.ObjectiveId)
            .ToDictionary(g => g.Key, g => g.First());

        var items = objectives.Select(objective =>
        {
            eventByObjectiveId.TryGetValue(objective.Id, out var eventMembership);
            var canEdit = IsEffectiveManager(objective) && !objective.IsAchieved && !objective.IsDefault;
            return new ProjectCalendarItemResponse(
                objective.Id,
                objective.ProjectId,
                objective.ParentObjectiveId,
                objective.Title,
                objective.StartDate,
                objective.EndDate,
                objective.IsActive,
                objective.IsAchieved,
                canEdit,
                eventMembership?.CalendarEventId,
                eventMembership?.Color);
        }).ToList();

        return Result<IReadOnlyList<ProjectCalendarItemResponse>>.Success(items);
    }
}
