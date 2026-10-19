using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// Task meetings for the C2 engine (spec 10 "Engine integration"): every materialized occurrence in the window the employee
/// organizes or effectively attends, keyed by its occurrence task id - a split keeps the task id, so it never duplicates an
/// incident. Its provider report is the occurrence's own attendance (spec 5.8), never the link's other sessions.
/// </summary>
public sealed class TaskMeetingOccurrenceSource : IEmployeeMeetingOccurrenceSource
{
    private const string PrivateTitle = "Private meeting";

    private readonly IMeetingRepository _meetings;
    private readonly ICalendarEventRepository _events;
    private readonly IMeetingAttendanceRepository _attendance;

    public TaskMeetingOccurrenceSource(IMeetingRepository meetings, ICalendarEventRepository events, IMeetingAttendanceRepository attendance)
    {
        _meetings = meetings;
        _events = events;
        _attendance = attendance;
    }

    public async Task<IReadOnlyList<CalendarMeetingView>> ListAsync(EmployeeOccurrenceContext context, CancellationToken ct)
    {
        var (tenantId, employee, from, to, identities, managerId, agent, _) = context;
        var rows = await _meetings.ListVisibleAsync(tenantId, employee.Id, from, to, null, null, ct);
        if (rows.Count == 0)
            return [];

        var seriesIds = rows.Select(r => r.Details.SeriesId).Distinct().ToList();
        var series = (await _meetings.ListSeriesAsync(tenantId, seriesIds, ct)).ToDictionary(s => s.Id);
        var participants = (await _meetings.ListParticipantsForSeriesAsync(tenantId, seriesIds, ct))
            .GroupBy(p => p.MeetingSeriesId!.Value).ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarEventParticipant>)g.ToList());
        var taskIds = rows.Select(r => r.Details.TaskId).ToList();
        var overrides = (await _meetings.ListOverridesAsync(tenantId, taskIds, ct))
            .GroupBy(o => o.OccurrenceTaskId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<MeetingOccurrenceAttendee>)g.ToList());
        // RSVP changes are recorded against the series (series answers) or the occurrence (an occurrence-only invitee).
        var history = (await _events.ListRsvpHistoryAsync(tenantId, employee.Id, seriesIds.Concat(taskIds).ToList(), ct))
            .GroupBy(h => h.EventId).ToDictionary(g => g.Key, g => g.ToList());

        var views = new List<CalendarMeetingView>();
        foreach (var row in rows)
        {
            var d = row.Details;
            if (!series.TryGetValue(d.SeriesId, out var s))
                continue;
            var people = participants.GetValueOrDefault(s.Id) ?? [];
            var mine = overrides.GetValueOrDefault(d.TaskId) ?? [];
            var attendees = EffectiveAttendees.Compute(people, [], mine);
            var isOrganizer = s.OrganizerEmployeeId == employee.Id;
            var me = attendees.FirstOrDefault(a => a.EmployeeId == employee.Id);
            if (me is null && !isOrganizer)
                continue;
            var peopleCount = attendees.Where(a => a.EmployeeId is not null).Select(a => a.EmployeeId!.Value)
                .Append(s.OrganizerEmployeeId).Distinct().Count();
            var changes = (history.GetValueOrDefault(s.Id) ?? []).Concat(history.GetValueOrDefault(d.TaskId) ?? [])
                .OrderBy(h => h.ChangedAt).Select(h => new CalendarRsvpChange(h.FromStatus, h.ToStatus, h.ChangedAt)).ToList();
            var (state, spans) = await ReportAsync(tenantId, d, employee.Id, identities, ct);

            views.Add(new CalendarMeetingView(
                d.TaskId, d.StartAt, d.EndAt, d.IsPrivate ? PrivateTitle : row.Title, d.IsAllDay, d.CancelledAt is not null, isOrganizer,
                peopleCount, s.OrganizerEmployeeId, managerId is not null && s.OrganizerEmployeeId == managerId,
                me?.Response ?? CalendarEventParticipantStatuses.Accepted, changes, ReplacementAttended: false,
                state, spans, agent.TryGetValue((d.TaskId, d.StartAt), out var ratio) ? ratio : null));
        }
        return views;
    }

    /// <summary>The occurrence's report state (spec 5.8 statuses) and, once synced, this employee's spans from its own rows.</summary>
    private async Task<(CalendarReportState State, IReadOnlyList<CalendarAttendanceSpan> Spans)> ReportAsync(Guid tenantId, MeetingDetails d,
        Guid employeeId, IReadOnlySet<string> identities, CancellationToken ct)
    {
        if (d.CalendarEventMeetingId is null)
            return (CalendarReportState.NoOnlineMeeting, []);
        switch (d.AttendanceStatus)
        {
            case MeetingAttendanceStatuses.Synced:
                var spans = (await _attendance.ListOccurrenceRecordsAsync(tenantId, d.TaskId, ct))
                    .Where(r => r.EmployeeId == employeeId || (r.ExternalParticipantEmail is { } email && identities.Contains(email)))
                    .OrderBy(r => r.JoinedAt)
                    .Select(r => new CalendarAttendanceSpan(r.JoinedAt, r.LeftAt)).ToList();
                return (CalendarReportState.Received, spans);
            case MeetingAttendanceStatuses.Unavailable or MeetingAttendanceStatuses.SyncFailed:
                return (CalendarReportState.Unavailable, []);
            case MeetingAttendanceStatuses.NotApplicable:
                return (CalendarReportState.NoOnlineMeeting, []);
            default:
                return (CalendarReportState.Pending, []);
        }
    }
}
