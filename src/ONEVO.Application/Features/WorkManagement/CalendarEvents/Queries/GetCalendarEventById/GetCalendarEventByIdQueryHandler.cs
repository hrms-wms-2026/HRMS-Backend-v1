using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventById;

public sealed class GetCalendarEventByIdQueryHandler : IRequestHandler<GetCalendarEventByIdQuery, Result<CalendarEventDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly IProjectMemberRepository _members;

    public GetCalendarEventByIdQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity,
        ICalendarEventRepository calendarEvents, IProjectMemberRepository members)
    {
        _currentUser = currentUser;
        _identity = identity;
        _calendarEvents = calendarEvents;
        _members = members;
    }

    public async Task<Result<CalendarEventDetailResponse>> Handle(GetCalendarEventByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<CalendarEventDetailResponse>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<CalendarEventDetailResponse>.Forbidden("Tenant context missing.");

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (employeeId is null)
            return Result<CalendarEventDetailResponse>.Forbidden("No employee record for the current user.");

        var calendarEvent = await _calendarEvents.GetByIdForTenantAsync(tenantId, request.Id, ct);
        if (calendarEvent is null)
            return Result<CalendarEventDetailResponse>.NotFound("Calendar event not found.");
        if (!await _members.HasActiveMembershipAsync(tenantId, calendarEvent.ProjectId, employeeId.Value, ct))
            return Result<CalendarEventDetailResponse>.Forbidden("You do not have access to this project.");

        var memberships = await _calendarEvents.ListMembershipsForEventAsync(calendarEvent.Id, ct);
        var taskLinks = await _calendarEvents.ListTaskMembershipsForEventAsync(calendarEvent.Id, ct);
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, new[] { calendarEvent.CreatedById }, ct);

        return Result<CalendarEventDetailResponse>.Success(new CalendarEventDetailResponse(
            calendarEvent.Id, calendarEvent.ProjectId, calendarEvent.Name, calendarEvent.Color, calendarEvent.Status,
            calendarEvent.StartDate, calendarEvent.EndDate, calendarEvent.Description,
            calendarEvent.CreatedById, names.GetValueOrDefault(calendarEvent.CreatedById),
            memberships.Select(m => m.ObjectiveId).ToList(), taskLinks.Select(l => l.TaskId).ToList(),
            calendarEvent.CreatedAt, calendarEvent.ArchivedById, calendarEvent.ArchivedAt));
    }
}
