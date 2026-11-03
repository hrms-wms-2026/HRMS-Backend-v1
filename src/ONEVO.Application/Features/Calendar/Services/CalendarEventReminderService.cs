using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>
/// The start reminder (plan C1 P2-2): every occurrence that starts within the next <see cref="LeadMinutes"/> minutes gets
/// one in-app reminder per participant who has not declined, at most once (calendar_event_reminders_sent). Single events
/// and moved occurrences come from their own start; a recurring series is expanded, skipping occurrences that were
/// cancelled or moved (a moved one is reminded from its own start, with the series' participants). Task meetings
/// (meetings Phase 2) are reminded per materialized occurrence - its effective attendees who have not declined, the organizer
/// included - logged against the occurrence task id and its start.
/// </summary>
public sealed class CalendarEventReminderService(
    ICalendarEventRepository events,
    ICalendarRecurrenceExpander expander,
    IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork,
    IMeetingRepository? taskMeetings = null)
{
    public const int LeadMinutes = 15;

    private sealed record Occurrence(Guid SeriesId, string Title, DateTimeOffset Start);

    /// <summary>A task-meeting occurrence's reminder recipients: effective attendees who have not declined, and its organizer.</summary>
    private async Task<IReadOnlyList<(Occurrence Occurrence, Guid EmployeeId)>> TaskMeetingRecipientsAsync(Guid tenantId, DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        if (taskMeetings is null)
            return [];
        var rows = await taskMeetings.ListStartingBetweenAsync(tenantId, from, to, ct);
        if (rows.Count == 0)
            return [];
        var seriesIds = rows.Select(r => r.Details.SeriesId).Distinct().ToList();
        var series = (await taskMeetings.ListSeriesAsync(tenantId, seriesIds, ct)).ToDictionary(s => s.Id);
        var participants = (await taskMeetings.ListParticipantsForSeriesAsync(tenantId, seriesIds, ct))
            .GroupBy(p => p.MeetingSeriesId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var overrides = (await taskMeetings.ListOverridesAsync(tenantId, rows.Select(r => r.Details.TaskId).ToList(), ct))
            .GroupBy(o => o.OccurrenceTaskId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<(Occurrence, Guid)>();
        foreach (var row in rows)
        {
            if (!series.TryGetValue(row.Details.SeriesId, out var s) || s.CancelledAt is not null)
                continue;
            var occurrence = new Occurrence(row.Details.TaskId, row.Title, row.Details.StartAt);
            var attendees = EffectiveAttendees.Compute(participants.GetValueOrDefault(s.Id) ?? [], [], overrides.GetValueOrDefault(row.Details.TaskId) ?? []);
            foreach (var employeeId in attendees
                         .Where(a => a.EmployeeId is not null && a.Response != CalendarEventParticipantStatuses.Rejected)
                         .Select(a => a.EmployeeId!.Value).Append(s.OrganizerEmployeeId).Distinct())
                result.Add((occurrence, employeeId));
        }
        return result;
    }

    public async Task<int> SendDueAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var to = now.AddMinutes(LeadMinutes);
        var occurrences = (await events.ListWithParticipantsStartingBetweenAsync(tenantId, now, to, ct))
            .Select(e => new Occurrence(e.RecurrenceParentId ?? e.Id, e.Title, e.StartDate))
            .ToList();
        foreach (var master in await events.ListRecurringMastersWithParticipantsAsync(tenantId, to, ct))
        {
            var starts = expander.Expand(master.RecurrenceRule!, master.StartDate, now, to).Where(s => s >= now && s < to).ToList();
            if (starts.Count == 0)
                continue;
            var overridden = (await events.GetChildrenForMasterAsync(tenantId, master.Id, ct))
                .Where(c => c.RecurrenceOriginalStart is not null).Select(c => c.RecurrenceOriginalStart!.Value).ToHashSet();
            occurrences.AddRange(starts.Where(s => !overridden.Contains(s)).Select(s => new Occurrence(master.Id, master.Title, s)));
        }
        var taskMeetingRecipients = await TaskMeetingRecipientsAsync(tenantId, now, to, ct);
        if (occurrences.Count == 0 && taskMeetingRecipients.Count == 0)
            return 0;

        var participants = occurrences.Count == 0 ? new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>()
            : await events.GetParticipantsForEventsAsync(tenantId, occurrences.Select(o => o.SeriesId).Distinct().ToList(), ct);
        var sent = await events.ListSentRemindersAsync(tenantId, now, to, ct);
        var due = occurrences
            .SelectMany(o => (participants.TryGetValue(o.SeriesId, out var list) ? list : [])
                .Where(p => p.ResponseStatus != CalendarEventParticipantStatuses.Rejected)
                .Select(p => (Occurrence: o, p.EmployeeId)))
            .Concat(taskMeetingRecipients)
            .Where(x => !sent.Contains((x.Occurrence.SeriesId, x.Occurrence.Start, x.EmployeeId)))
            .DistinctBy(x => (x.Occurrence.SeriesId, x.Occurrence.Start, x.EmployeeId))
            .ToList();
        if (due.Count == 0)
            return 0;

        var people = await employees.ListByIdsAsync(tenantId, due.Select(x => x.EmployeeId).Distinct().ToList(), ct);
        var logs = new List<CalendarEventReminderLog>();
        foreach (var (occurrence, employeeId) in due)
        {
            if (!people.TryGetValue(employeeId, out var person) || person.UserId == Guid.Empty)
                continue;
            await notifications.NotifyEventReminderAsync(tenantId, person.UserId, occurrence.Title, occurrence.Start, ct);
            logs.Add(new CalendarEventReminderLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EventId = occurrence.SeriesId, OccurrenceStart = occurrence.Start,
                EmployeeId = employeeId, SentAt = now, CreatedAt = now,
            });
        }
        if (logs.Count == 0)
            return 0;
        await events.AddReminderLogsAsync(logs, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return logs.Count;
    }
}
