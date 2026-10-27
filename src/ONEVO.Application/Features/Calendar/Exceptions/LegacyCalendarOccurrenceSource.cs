using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.Calendar.Exceptions;

/// <summary>
/// Calendar events as the C2 rules see them (plan C2): single events and moved occurrences as they are, recurring series
/// expanded (cancelled or moved occurrences skipped - a moved one is its own row); a series shares one provider report.
/// </summary>
public sealed class LegacyCalendarOccurrenceSource(
    ICalendarEventRepository events,
    ICalendarEventMeetingRepository meetings,
    ICalendarEventMeetingAttendanceRepository attendances,
    ICalendarRecurrenceExpander expander,
    CoreEmployeeRepository employees) : IEmployeeMeetingOccurrenceSource
{
    public async Task<IReadOnlyList<CalendarMeetingView>> ListAsync(EmployeeOccurrenceContext context, CancellationToken ct)
    {
        var (tenantId, employee, from, to, identities, managerId, agent, now) = context;
        var employeeId = employee.Id;

        var occurrences = new List<(CalendarEvent Event, Guid SeriesId, DateTimeOffset Start, DateTimeOffset End, bool Cancelled)>();
        foreach (var e in await events.GetInDateRangeForEmployeeAsync(tenantId, employeeId, from, to, ct))
            occurrences.Add((e, e.RecurrenceParentId ?? e.Id, e.StartDate, e.EndDate, IsCancelled(e)));
        foreach (var master in await events.GetRecurringMastersForEmployeeAsync(tenantId, employeeId, to, ct))
        {
            if (string.IsNullOrWhiteSpace(master.RecurrenceRule))
                continue;
            var overridden = (await events.GetChildrenForMasterAsync(tenantId, master.Id, ct))
                .Where(c => c.RecurrenceOriginalStart is not null).Select(c => c.RecurrenceOriginalStart!.Value).ToHashSet();
            var length = master.EndDate - master.StartDate;
            foreach (var start in expander.Expand(master.RecurrenceRule, master.StartDate, from, to).Where(s => !overridden.Contains(s)))
                occurrences.Add((master, master.Id, start, start + length, IsCancelled(master)));
        }

        var seriesIds = occurrences.Select(o => o.SeriesId).Distinct().ToList();
        var participants = seriesIds.Count == 0 ? new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>()
            : await events.GetParticipantsForEventsAsync(tenantId, seriesIds, ct);
        var history = (await events.ListRsvpHistoryAsync(tenantId, employeeId, seriesIds, ct))
            .GroupBy(h => h.EventId).ToDictionary(g => g.Key, g => g.ToList());
        var organizerIds = new Dictionary<Guid, Guid?>();

        var views = new List<CalendarMeetingView>();
        var reports = new Dictionary<Guid, (CalendarEventMeeting? Meeting, IReadOnlyList<CalendarEventMeetingAttendance> Rows)>();
        foreach (var (e, seriesId, start, end, cancelled) in occurrences)
        {
            var people = participants.TryGetValue(seriesId, out var list) ? list : [];
            var mine = people.FirstOrDefault(p => p.EmployeeId == employeeId);
            var isOrganizer = e.CreatedById == employee.UserId;
            if (mine is null && !isOrganizer)
                continue;
            if (!organizerIds.TryGetValue(e.CreatedById, out var organizerId))
                organizerIds[e.CreatedById] = organizerId = (await employees.GetDefaultForUserAsync(tenantId, e.CreatedById, ct))?.Id;
            var peopleCount = people.Select(p => p.EmployeeId).Append(organizerId ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct().Count();

            if (!reports.TryGetValue(seriesId, out var report))
            {
                var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, seriesId, ct);
                var rows = meeting is { Status: CalendarEventMeetingStatuses.Active } && meeting.LastAttendanceSyncedAt is not null
                    ? await attendances.GetByMeetingIdAsync(tenantId, meeting.Id, ct) : [];
                reports[seriesId] = report = (meeting, rows);
            }
            var (state, spans) = CalendarIncidentEngine.ReportFor(report.Meeting, report.Rows, start, end, employeeId, identities, now);

            views.Add(new CalendarMeetingView(
                seriesId, start, end, CalendarIncidentEngine.DisplayTitle(e), e.IsAllDay, cancelled, isOrganizer, peopleCount, organizerId,
                organizerId is not null && organizerId == managerId,
                mine?.ResponseStatus ?? CalendarEventParticipantStatuses.Accepted,
                history.TryGetValue(seriesId, out var changes) ? changes.Select(h => new CalendarRsvpChange(h.FromStatus, h.ToStatus, h.ChangedAt)).ToList() : [],
                // The nominee of a replacement is not stored, so a replacement's attendance cannot be confirmed.
                ReplacementAttended: false,
                state, spans, agent.TryGetValue((seriesId, start), out var ratio) ? ratio : null));
        }
        return views;
    }

    private static bool IsCancelled(CalendarEvent e) => e.IsRecurrenceCancelled || e.EventStatus == CalendarEventStatuses.Cancelled;
}
