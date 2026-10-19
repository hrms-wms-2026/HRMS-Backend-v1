using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>
/// The start reminder (plan C1 P2-2): every occurrence that starts within the next <see cref="LeadMinutes"/> minutes gets
/// one in-app reminder per participant who has not declined, at most once (calendar_event_reminders_sent). Single events
/// and moved occurrences come from their own start; a recurring series is expanded, skipping occurrences that were
/// cancelled or moved (a moved one is reminded from its own start, with the series' participants).
/// </summary>
public sealed class CalendarEventReminderService(
    ICalendarEventRepository events,
    ICalendarRecurrenceExpander expander,
    IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork)
{
    public const int LeadMinutes = 15;

    private sealed record Occurrence(Guid SeriesId, string Title, DateTimeOffset Start);

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
        if (occurrences.Count == 0)
            return 0;

        var participants = await events.GetParticipantsForEventsAsync(tenantId, occurrences.Select(o => o.SeriesId).Distinct().ToList(), ct);
        var sent = await events.ListSentRemindersAsync(tenantId, now, to, ct);
        var due = occurrences
            .SelectMany(o => (participants.TryGetValue(o.SeriesId, out var list) ? list : [])
                .Where(p => p.ResponseStatus != CalendarEventParticipantStatuses.Rejected)
                .Select(p => (Occurrence: o, p.EmployeeId)))
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
