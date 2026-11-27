using MediatR;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

public sealed class GetCalendarEventsQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ICalendarEventRepository events,
    ICalendarRecurrenceExpander expander,
    MeetingCalendarProjection? meetings = null)
    : IRequestHandler<GetCalendarEventsQuery, Result<CalendarEventsResponse>>
{
    private static readonly Guid OccurrenceIdNamespace = Guid.Parse("6f1f9b2a-6c1e-4b7a-9c2e-8f6a1d2b3c4d");

    public async Task<Result<CalendarEventsResponse>> Handle(GetCalendarEventsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventsResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);

        var realRows = await events.GetInDateRangeForCallerAsync(
            tenantId, currentUser.UserId, employee?.Id, request.From, request.To, ct);

        var masters = await events.GetRecurringMastersForCallerAsync(tenantId, currentUser.UserId, employee?.Id, request.To, ct);

        // Participants, guests, children and participant names are each loaded once for every event (Improvements
        // task 14), not once per event or per participant.
        var eventIds = realRows.Select(e => e.Id).Concat(masters.Select(m => m.Id)).ToList();
        var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, eventIds, ct);
        var guestsByEvent = await events.GetGuestsForEventsAsync(tenantId, eventIds, ct);
        var childrenByMaster = await events.GetChildrenForMastersAsync(tenantId, masters.Select(m => m.Id).ToList(), ct);
        var participantEmployees = await employees.ListByIdsAsync(
            tenantId, participantsByEvent.Values.SelectMany(p => p).Select(p => p.EmployeeId).Distinct().ToList(), ct);

        var items = new List<CalendarEventItem>();
        foreach (var e in realRows)
        {
            items.Add(new CalendarEventItem(
                e.Id, e.Title, e.Description, e.StartDate, e.EndDate, e.SourceType, e.Color,
                e.Recurrence, e.IsAllDay, e.Timezone, e.EventStatus, e.IsPrivate, e.Location,
                e.MeetingLink, e.ExternalSource, e.CreatedById,
                Participants: ParticipantSummaries(participantsByEvent, participantEmployees, e.Id),
                Guests: GuestSummaries(guestsByEvent, e.Id)));
        }

        foreach (var master in masters)
        {
            var children = childrenByMaster.GetValueOrDefault(master.Id) ?? [];
            var occurrenceStarts = expander.Expand(master.RecurrenceRule!, master.StartDate, request.From, request.To);
            var duration = master.EndDate - master.StartDate;

            var masterGuests = GuestSummaries(guestsByEvent, master.Id);
            var masterParticipants = ParticipantSummaries(participantsByEvent, participantEmployees, master.Id);

            foreach (var occurrenceStart in occurrenceStarts)
            {
                var overridden = children.Any(c => c.RecurrenceOriginalStart == occurrenceStart);
                if (overridden)
                    continue; // a detached row is already in realRows; a cancellation is never shown

                var occurrenceId = DeterministicGuid.Create(OccurrenceIdNamespace, $"{master.Id:N}|{occurrenceStart:O}");
                items.Add(new CalendarEventItem(
                    occurrenceId, master.Title, master.Description, occurrenceStart, occurrenceStart + duration,
                    master.SourceType, master.Color, master.Recurrence, master.IsAllDay, master.Timezone,
                    master.EventStatus, master.IsPrivate, master.Location, master.MeetingLink,
                    master.ExternalSource, master.CreatedById,
                    IsRecurringOccurrence: true, RecurrenceMasterId: master.Id, OriginalStart: occurrenceStart,
                    Participants: masterParticipants, Guests: masterGuests));
            }
        }

        // Task meetings (meetings Phase 2) sit next to calendar events, and count for the conflict flags (spec 5.9).
        if (meetings is not null && employee is not null)
            items.AddRange(await meetings.ListAsync(tenantId, employee.Id, request.From, request.To, ct));

        var creatorIds = items.Select(i => i.CreatedById).Distinct().ToList();
        var otherOrganizers = await employees.ListDefaultForUsersAsync(
            tenantId, creatorIds.Where(id => id != currentUser.UserId).ToList(), ct);
        var organizerNames = new Dictionary<Guid, string?>();
        foreach (var creatorId in creatorIds)
        {
            var organizer = creatorId == currentUser.UserId ? employee : otherOrganizers.GetValueOrDefault(creatorId);
            organizerNames[creatorId] = organizer is null ? null : $"{organizer.FirstName} {organizer.LastName}";
        }

        var withConflictFlags = items.Select(item => item with
        {
            HasConflict = items.Any(other => other.Id != item.Id && other.StartDate < item.EndDate && item.StartDate < other.EndDate),
            IsOrganizer = item.CreatedById == currentUser.UserId,
            OrganizerName = organizerNames[item.CreatedById]
        }).ToList();

        return Result<CalendarEventsResponse>.Success(new CalendarEventsResponse(withConflictFlags.OrderBy(i => i.StartDate).ToList()));
    }

    private static IReadOnlyList<CalendarEventGuestSummary> GuestSummaries(
        IReadOnlyDictionary<Guid, IReadOnlyList<CalendarEventGuest>> guestsByEvent, Guid eventId)
        => guestsByEvent.TryGetValue(eventId, out var guests) ? guests.Select(g => new CalendarEventGuestSummary(g.Email)).ToList() : [];

    private static IReadOnlyList<CalendarEventParticipantSummary> ParticipantSummaries(
        IReadOnlyDictionary<Guid, IReadOnlyList<CalendarEventParticipant>> participantsByEvent,
        IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee> employeesById, Guid eventId)
        => participantsByEvent.TryGetValue(eventId, out var participants)
            ? participants.Select(p => new CalendarEventParticipantSummary(
                p.EmployeeId,
                employeesById.TryGetValue(p.EmployeeId, out var employee) ? $"{employee.FirstName} {employee.LastName}" : "Unknown",
                p.ResponseStatus)).ToList()
            : [];
}
