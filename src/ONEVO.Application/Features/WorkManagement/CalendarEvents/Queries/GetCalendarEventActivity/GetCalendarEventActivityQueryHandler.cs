using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventActivity;

public sealed class GetCalendarEventActivityQueryHandler : IRequestHandler<GetCalendarEventActivityQuery, Result<IReadOnlyList<CalendarEventActivityEntryResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly ICalendarEventActivityLogRepository _activityLogs;
    private readonly IProjectMemberRepository _members;

    public GetCalendarEventActivityQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ICalendarEventRepository calendarEvents,
        ICalendarEventActivityLogRepository activityLogs, IProjectMemberRepository members)
    {
        _currentUser = currentUser;
        _identity = identity;
        _calendarEvents = calendarEvents;
        _activityLogs = activityLogs;
        _members = members;
    }

    public async Task<Result<IReadOnlyList<CalendarEventActivityEntryResponse>>> Handle(GetCalendarEventActivityQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Forbidden("Tenant context missing.");

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (employeeId is null)
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Forbidden("No employee record for the current user.");

        var calendarEvent = await _calendarEvents.GetByIdForTenantAsync(tenantId, request.CalendarEventId, ct);
        if (calendarEvent is null)
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.NotFound("Calendar event not found.");
        if (!await _members.HasActiveMembershipAsync(tenantId, calendarEvent.ProjectId, employeeId.Value, ct))
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Forbidden("You do not have access to this project.");

        var entries = await _activityLogs.ListByEventIdAsync(calendarEvent.Id, ct);
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, entries.Select(e => e.PerformedById).Distinct().ToList(), ct);

        var responses = entries.Select(e => new CalendarEventActivityEntryResponse(
            e.Id, e.Action, e.PerformedById, names.GetValueOrDefault(e.PerformedById), e.PerformedAt, e.DetailsJson)).ToList();

        return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Success(responses);
    }
}
