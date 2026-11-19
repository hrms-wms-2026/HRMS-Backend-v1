using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// A caller's task meetings as calendar items (spec 5.9): the materialized occurrences they may see (MeetingVisibility) and
/// the virtual occurrences further out of every series they organize or attend - each with the id it has, or will get, as
/// a task (<see cref="MeetingOccurrenceIds"/>). Cancelled occurrences, suppressed slots and cancelled series are left out.
/// </summary>
public sealed class MeetingCalendarProjection
{
    public const string ItemKind = "meeting";
    private const string SourceType = "meeting";

    private readonly IMeetingRepository _meetings;
    private readonly IMeetingLinkRepository _links;
    private readonly IEmployeeRepository _employees;
    private readonly IMeetingRecurrence _recurrence;
    private readonly IDateTimeProvider _clock;

    public MeetingCalendarProjection(IMeetingRepository meetings, IMeetingLinkRepository links, IEmployeeRepository employees,
        IMeetingRecurrence recurrence, IDateTimeProvider clock)
    {
        _meetings = meetings;
        _links = links;
        _employees = employees;
        _recurrence = recurrence;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CalendarEventItem>> ListAsync(Guid tenantId, Guid callerEmployeeId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var rows = (await _meetings.ListVisibleAsync(tenantId, callerEmployeeId, from, to, null, null, ct))
            .Where(r => r.Details.CancelledAt is null).ToList();
        var attended = await _meetings.ListSeriesForAttendeeAsync(tenantId, callerEmployeeId, ct);
        var series = attended.ToDictionary(s => s.Id);
        var missing = rows.Select(r => r.Details.SeriesId).Where(id => !series.ContainsKey(id)).Distinct().ToList();
        if (missing.Count > 0)
            foreach (var s in await _meetings.ListSeriesAsync(tenantId, missing, ct))
                series[s.Id] = s;

        // Everyone's names in one read: the organizers and every series' participants.
        var participants = await _meetings.ListParticipantsForSeriesAsync(tenantId, series.Keys.ToList(), ct);
        var organizers = await _employees.ListByIdsAsync(tenantId,
            series.Values.Select(s => s.OrganizerEmployeeId).Concat(participants.Select(p => p.EmployeeId)).Distinct().ToList(), ct);
        var attendees = series.Values.ToDictionary(s => s.Id, s => Attendees(s, participants, organizers));
        var joinUrls = new Dictionary<Guid, string?>();
        foreach (var id in series.Keys)
            joinUrls[id] = await _links.GetActiveForSeriesAsync(tenantId, id, ct) is { Status: CalendarEventMeetingStatuses.Active } link
                && !string.IsNullOrEmpty(link.JoinUrl) ? link.JoinUrl : null;

        var items = new List<CalendarEventItem>();
        foreach (var row in rows)
            if (series.TryGetValue(row.Details.SeriesId, out var s))
                items.Add(Item(s, row.Details, row.Details.TaskId, row.Details.TaskId, row.Title, row.Description, organizers, joinUrls, attendees, now));

        // Virtual occurrences: a slot nobody materialized or suppressed yet, from the series template.
        var open = attended.Where(s => s.CancelledAt is null).ToList();
        var longest = open.Count == 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(open.Max(s => s.DurationMinutes));
        var taken = await _meetings.ListTakenSlotsAsync(tenantId, open.Select(s => s.Id).ToList(), from - longest, to, ct);
        foreach (var s in open)
            foreach (var start in _recurrence.Occurrences(s.RecurrenceRule, s.DtStart, s.Timezone, from - TimeSpan.FromMinutes(s.DurationMinutes), to))
            {
                if (start.AddMinutes(s.DurationMinutes) <= from || taken.Contains((s.Id, start.ToUniversalTime())))
                    continue;
                var slot = new MeetingDetails
                {
                    SeriesId = s.Id, OriginalStart = start, StartAt = start, EndAt = start.AddMinutes(s.DurationMinutes), IsAllDay = s.IsAllDay,
                    Location = s.Location, IsPrivate = s.IsPrivate,
                };
                items.Add(Item(s, slot, MeetingOccurrenceIds.For(s.Id, start), null, s.Title, s.Description, organizers, joinUrls, attendees, now));
            }
        return items.OrderBy(i => i.StartDate).ToList();
    }

    private static CalendarEventItem Item(MeetingSeries s, MeetingDetails d, Guid id, Guid? taskId, string title, string? description,
        IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee> organizers, IReadOnlyDictionary<Guid, string?> joinUrls,
        IReadOnlyDictionary<Guid, IReadOnlyList<CalendarEventParticipantSummary>> attendees, DateTimeOffset now)
        => new(id, title, description, d.StartAt, d.EndAt, SourceType, Color: null, RecurrenceOf(s.RecurrenceRule), d.IsAllDay, s.Timezone,
            CalendarEventStatuses.Confirmed, d.IsPrivate, d.Location, joinUrls.GetValueOrDefault(s.Id), ExternalSource: null,
            CreatedById: organizers.TryGetValue(s.OrganizerEmployeeId, out var organizer) ? organizer.UserId : Guid.Empty,
            IsRecurringOccurrence: s.RecurrenceRule is not null, OriginalStart: d.OriginalStart,
            Participants: attendees.GetValueOrDefault(s.Id),
            ItemKind: ItemKind, MeetingSeriesId: s.Id, MeetingTaskId: taskId, MeetingStatus: MeetingStatusCalculator.Of(d, s, now));

    /// <summary>
    /// A series' people for the calendar (avatars on the meeting block): the organizer first, then the participants by name.
    /// Series level only; occurrence-only additions are not reflected here.
    /// </summary>
    private static IReadOnlyList<CalendarEventParticipantSummary> Attendees(MeetingSeries s, IReadOnlyList<CalendarEventParticipant> participants,
        IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee> people)
    {
        string NameOf(Guid id) => people.TryGetValue(id, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : string.Empty;
        var own = participants.Where(p => p.MeetingSeriesId == s.Id).ToList();
        var organizerStatus = own.FirstOrDefault(p => p.EmployeeId == s.OrganizerEmployeeId)?.ResponseStatus
            ?? CalendarEventParticipantStatuses.Accepted;
        return [
            new CalendarEventParticipantSummary(s.OrganizerEmployeeId, NameOf(s.OrganizerEmployeeId), organizerStatus),
            .. own.Where(p => p.EmployeeId != s.OrganizerEmployeeId)
                .Select(p => new CalendarEventParticipantSummary(p.EmployeeId, NameOf(p.EmployeeId), p.ResponseStatus))
                .OrderBy(p => p.EmployeeName, StringComparer.CurrentCulture),
        ];
    }

    /// <summary>The legacy calendar's coarse recurrence label for an RRULE.</summary>
    private static string RecurrenceOf(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
            return CalendarRecurrences.None;
        var upper = rule.ToUpperInvariant();
        return upper.Contains("FREQ=DAILY", StringComparison.Ordinal) ? CalendarRecurrences.Daily
            : upper.Contains("FREQ=WEEKLY", StringComparison.Ordinal) ? CalendarRecurrences.Weekly
            : upper.Contains("FREQ=MONTHLY", StringComparison.Ordinal) ? CalendarRecurrences.Monthly
            : CalendarRecurrences.None;
    }
}
