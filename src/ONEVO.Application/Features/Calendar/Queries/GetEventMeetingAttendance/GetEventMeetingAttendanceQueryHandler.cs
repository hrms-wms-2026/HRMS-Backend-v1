using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;

public sealed class GetEventMeetingAttendanceQueryHandler(
    ICurrentUser currentUser,
    ICalendarEventMeetingRepository meetings,
    ICalendarEventMeetingAttendanceRepository attendances,
    ICalendarEventRepository? events = null,
    ICalendarIncidentRepository? incidents = null,
    IEmployeeRepository? employees = null)
    : IRequestHandler<GetEventMeetingAttendanceQuery, Result<GetEventMeetingAttendanceResult>>
{
    public async Task<Result<GetEventMeetingAttendanceResult>> Handle(GetEventMeetingAttendanceQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<GetEventMeetingAttendanceResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var raised = await OrganizerIncidentsAsync(tenantId, request.EventId, ct);
        var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, request.EventId, ct);
        if (meeting is null || meeting.LastAttendanceSyncedAt is null)
            return Result<GetEventMeetingAttendanceResult>.Success(new GetEventMeetingAttendanceResult(false, [], raised));

        var records = await attendances.GetByMeetingIdAsync(tenantId, meeting.Id, ct);
        var items = records.Select(a => new MeetingAttendeeItem(
            a.ExternalParticipantName, a.ExternalParticipantEmail, a.JoinedAt, a.LeftAt, a.DurationSeconds)).ToList();
        return Result<GetEventMeetingAttendanceResult>.Success(new GetEventMeetingAttendanceResult(true, items, raised));
    }

    /// <summary>The incidents this meeting raised - for its organizer only: rule and occurrence, never the evidence.</summary>
    private async Task<IReadOnlyList<MeetingIncidentItem>> OrganizerIncidentsAsync(Guid tenantId, Guid eventId, CancellationToken ct)
    {
        if (events is null || incidents is null || employees is null)
            return [];
        var calendarEvent = await events.GetByIdForTenantAsync(tenantId, eventId, ct);
        if (calendarEvent is null || calendarEvent.CreatedById != currentUser.UserId)
            return [];
        var rows = await incidents.ListForEventAsync(tenantId, eventId, ct);
        if (rows.Count == 0)
            return [];
        var people = await employees.ListByIdsAsync(tenantId, rows.Select(r => r.EmployeeId).Distinct().ToList(), ct);
        return rows.Select(r => new MeetingIncidentItem(
            people.TryGetValue(r.EmployeeId, out var person) ? $"{person.FirstName} {person.LastName}".Trim() : "Participant",
            CalendarRuleCatalog.Specs.TryGetValue(r.RuleCode, out var spec) ? spec.Title : r.RuleCode,
            r.OccurrenceStart,
            r.Confidence == CalendarIncidentConfidence.Agent)).ToList();
    }
}
