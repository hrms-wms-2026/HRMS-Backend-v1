using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>The .ics identity of a Phase 2 meeting (spec 3): UID = series id; one occurrence adds RECURRENCE-ID =
/// its original start; SEQUENCE from the time of the change.</summary>
public static class MeetingIcs
{
    public static CalendarEventIcs ForSeries(MeetingSeries series, DateTimeOffset now)
        => CalendarEventIcs.For(series.Id, series.DtStart.AddMinutes(series.DurationMinutes), series.RecurrenceRule, now);

    public static CalendarEventIcs ForOccurrence(MeetingSeries series, MeetingDetails occurrence, DateTimeOffset now)
        => new(series.Id, occurrence.EndAt, CalendarIcs.Sequence(now), RecurrenceRule: null, RecurrenceId: occurrence.OriginalStart);
}

/// <summary>
/// Every email and in-app notice a Phase 2 meeting sends its attendees (spec 3, 5.2, 5.3). Each email is keyed by its
/// recipient row (<see cref="CalendarEmailKeys"/>) so a retried change emails nobody twice, and carries the meeting's
/// .ics. The organizer is never emailed about their own meeting. Runs inside the caller's transaction; never saves.
/// </summary>
public interface IMeetingNotifier
{
    /// <summary>Invites to newly added series attendees - held back while the series' link is being provisioned (the link
    /// email then doubles as the invite, spec 5.7). <paramref name="reinvited"/> maps a revived participant row to the moment it
    /// was removed, so the re-invite is a new email.</summary>
    Task InviteAsync(MeetingSeries series, IReadOnlyList<CalendarEventParticipant> participants, IReadOnlyList<CalendarEventGuest> guests,
        CancellationToken ct, IReadOnlyDictionary<Guid, DateTimeOffset>? reinvited = null);

    /// <summary>Invites to people added to one occurrence only. Every row in <paramref name="added"/> now attends it: an
    /// "add" override, or the "remove" override that was dropped when a series attendee was put back on.</summary>
    Task InviteToOccurrenceAsync(MeetingSeries series, MeetingDetails occurrence, string title, IReadOnlyList<MeetingOccurrenceAttendee> added,
        CancellationToken ct);

    /// <summary>A series-wide change (or a new series after a split): every series attendee.</summary>
    Task SeriesUpdatedAsync(MeetingSeries series, CancellationToken ct);

    /// <summary>A change to one occurrence: its effective attendees.</summary>
    Task OccurrenceUpdatedAsync(MeetingSeries series, MeetingDetails occurrence, string title, CancellationToken ct);

    Task OccurrenceCancelledAsync(MeetingSeries series, MeetingDetails occurrence, string title, CancellationToken ct);

    Task SeriesCancelledAsync(MeetingSeries series, CancellationToken ct);

    /// <summary>One person taken off the series (<paramref name="occurrence"/> null) or off one occurrence.</summary>
    Task RemovedAsync(MeetingSeries series, MeetingDetails? occurrence, string title, Guid rowId, Guid? employeeId, string? email,
        CancellationToken ct);

    /// <summary>The organizer's in-app notice that someone accepted or declined.</summary>
    Task RsvpAnsweredAsync(MeetingSeries series, string title, Guid responderEmployeeId, bool accepted, CancellationToken ct);

    /// <summary>The series' new join link to every series attendee, keyed link:{linkId}:{generation}:{row}; while the
    /// invites were held back this email is the invite (spec 5.7).</summary>
    Task LinkReadyAsync(MeetingSeries series, CalendarEventMeeting link, CancellationToken ct);

    /// <summary>The link went away for this series: keyed link-removed:{linkId}:{generation}:{row} to its attendees.</summary>
    Task LinkRemovedAsync(MeetingSeries series, CalendarEventMeeting link, CancellationToken ct);

    /// <summary>In-app notice to the organizer about the link (CalendarMeetingNoticeCodes).</summary>
    Task NotifyOrganizerAsync(MeetingSeries series, string noticeCode, string provider, CancellationToken ct);
}

public sealed class MeetingNotifier : IMeetingNotifier
{
    private readonly ICalendarNotificationSender _sender;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingLinkRepository _links;
    private readonly IEmployeeRepository _employees;
    private readonly IDateTimeProvider _clock;

    public MeetingNotifier(ICalendarNotificationSender sender, IMeetingRepository meetings, IMeetingLinkRepository links,
        IEmployeeRepository employees, IDateTimeProvider clock)
    {
        _sender = sender;
        _meetings = meetings;
        _links = links;
        _employees = employees;
        _clock = clock;
    }

    public async Task InviteAsync(MeetingSeries series, IReadOnlyList<CalendarEventParticipant> participants,
        IReadOnlyList<CalendarEventGuest> guests, CancellationToken ct, IReadOnlyDictionary<Guid, DateTimeOffset>? reinvited = null)
    {
        var link = await _links.GetActiveForSeriesAsync(series.TenantId, series.Id, ct);
        if (link is { InvitesPending: true })
            return;
        var invitees = participants.Where(p => p.EmployeeId != series.OrganizerEmployeeId).ToList();
        if (invitees.Count == 0 && guests.Count == 0)
            return;
        var organizer = await OrganizerNameAsync(series, ct);
        var joinUrl = JoinUrl(link);
        if (invitees.Count > 0)
            await _sender.NotifyParticipantsAddedAsync(series.TenantId, series.Title, series.DtStart, series.Location,
                invitees.Select(p => p.EmployeeId).ToList(), organizer, ct, joinUrl, sendInviteEmail: false);
        await _sender.SendMeetingLinkAsync(series.TenantId, series.Title, series.DtStart, series.Location,
            invitees.Select(p => new CalendarParticipantRecipient(p.EmployeeId, reinvited is not null && reinvited.TryGetValue(p.Id, out var removedAt)
                ? CalendarEmailKeys.ReInvite(p.Id, removedAt) : CalendarEmailKeys.Invite(p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Invite(g.Id))).ToList(),
            organizer, joinUrl, MeetingIcs.ForSeries(series, _clock.UtcNow), ct);
    }

    public async Task InviteToOccurrenceAsync(MeetingSeries series, MeetingDetails occurrence, string title,
        IReadOnlyList<MeetingOccurrenceAttendee> added, CancellationToken ct)
    {
        var people = added;
        var employees = people.Where(a => a.EmployeeId is Guid id && id != series.OrganizerEmployeeId).ToList();
        var guests = people.Where(a => a.AttendeeKind == MeetingAttendeeKinds.Guest && a.Email is not null).ToList();
        if (employees.Count == 0 && guests.Count == 0)
            return;
        var organizer = await OrganizerNameAsync(series, ct);
        var joinUrl = JoinUrl(await _links.GetActiveForSeriesAsync(series.TenantId, series.Id, ct));
        if (employees.Count > 0)
            await _sender.NotifyParticipantsAddedAsync(series.TenantId, title, occurrence.StartAt, occurrence.Location,
                employees.Select(a => a.EmployeeId!.Value).ToList(), organizer, ct, joinUrl, sendInviteEmail: false);
        await _sender.SendMeetingLinkAsync(series.TenantId, title, occurrence.StartAt, occurrence.Location,
            employees.Select(a => new CalendarParticipantRecipient(a.EmployeeId!.Value, CalendarEmailKeys.Invite(a.Id))).ToList(),
            guests.Select(a => new CalendarGuestRecipient(a.Email!, CalendarEmailKeys.Invite(a.Id))).ToList(),
            organizer, joinUrl, MeetingIcs.ForOccurrence(series, occurrence, _clock.UtcNow), ct);
    }

    public async Task SeriesUpdatedAsync(MeetingSeries series, CancellationToken ct)
    {
        var (participants, guests) = await SeriesRecipientsAsync(series, row => CalendarEmailKeys.Update(series.Id, series.Revision, row), ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        var organizer = await OrganizerNameAsync(series, ct);
        var joinUrl = JoinUrl(await _links.GetActiveForSeriesAsync(series.TenantId, series.Id, ct));
        await _sender.NotifyEventUpdatedAsync(series.TenantId, series.Title, participants.Select(p => p.EmployeeId).ToList(), organizer, ct);
        await _sender.EmailEventChangedKeyedAsync(series.TenantId, CalendarEventEmailKinds.Updated, MeetingIcs.ForSeries(series, _clock.UtcNow),
            series.Title, series.DtStart, series.Location, joinUrl, participants, guests, organizer, ct);
    }

    public async Task OccurrenceUpdatedAsync(MeetingSeries series, MeetingDetails occurrence, string title, CancellationToken ct)
    {
        var (participants, guests) = await OccurrenceRecipientsAsync(series, occurrence,
            row => CalendarEmailKeys.Update(occurrence.TaskId, occurrence.Revision, row), ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        var organizer = await OrganizerNameAsync(series, ct);
        var joinUrl = JoinUrl(await _links.GetActiveForSeriesAsync(series.TenantId, series.Id, ct));
        await _sender.NotifyEventUpdatedAsync(series.TenantId, title, participants.Select(p => p.EmployeeId).ToList(), organizer, ct);
        await _sender.EmailEventChangedKeyedAsync(series.TenantId, CalendarEventEmailKinds.Updated,
            MeetingIcs.ForOccurrence(series, occurrence, _clock.UtcNow), title, occurrence.StartAt, occurrence.Location, joinUrl,
            participants, guests, organizer, ct);
    }

    public async Task OccurrenceCancelledAsync(MeetingSeries series, MeetingDetails occurrence, string title, CancellationToken ct)
    {
        var (participants, guests) = await OccurrenceRecipientsAsync(series, occurrence,
            row => CalendarEmailKeys.Cancel(series.Id, occurrence.OriginalStart, row), ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        var organizer = await OrganizerNameAsync(series, ct);
        await _sender.NotifyEventCancelledAsync(series.TenantId, title, participants.Select(p => p.EmployeeId).ToList(), organizer, ct);
        await _sender.EmailEventChangedKeyedAsync(series.TenantId, CalendarEventEmailKinds.Cancelled,
            MeetingIcs.ForOccurrence(series, occurrence, _clock.UtcNow), title, occurrence.StartAt, occurrence.Location, null,
            participants, guests, organizer, ct);
    }

    public async Task SeriesCancelledAsync(MeetingSeries series, CancellationToken ct)
    {
        var (participants, guests) = await SeriesRecipientsAsync(series, row => CalendarEmailKeys.Cancel(series.Id, null, row), ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        var organizer = await OrganizerNameAsync(series, ct);
        await _sender.NotifyEventCancelledAsync(series.TenantId, series.Title, participants.Select(p => p.EmployeeId).ToList(), organizer, ct);
        await _sender.EmailEventChangedKeyedAsync(series.TenantId, CalendarEventEmailKinds.Cancelled, MeetingIcs.ForSeries(series, _clock.UtcNow),
            series.Title, series.DtStart, series.Location, null, participants, guests, organizer, ct);
    }

    public async Task RemovedAsync(MeetingSeries series, MeetingDetails? occurrence, string title, Guid rowId, Guid? employeeId, string? email,
        CancellationToken ct)
    {
        if (employeeId == series.OrganizerEmployeeId)
            return;
        var organizer = await OrganizerNameAsync(series, ct);
        var now = _clock.UtcNow;
        var key = CalendarEmailKeys.Removed(series.Id, occurrence?.OriginalStart, rowId, now);
        IReadOnlyList<CalendarParticipantRecipient> participants = employeeId is Guid id ? [new CalendarParticipantRecipient(id, key)] : [];
        IReadOnlyList<CalendarGuestRecipient> guests = employeeId is null && email is not null ? [new CalendarGuestRecipient(email, key)] : [];
        if (employeeId is Guid removed)
            await _sender.NotifyParticipantRemovedAsync(series.TenantId, removed, title, organizer, ct);
        await _sender.EmailEventChangedKeyedAsync(series.TenantId, CalendarEventEmailKinds.Removed,
            occurrence is null ? MeetingIcs.ForSeries(series, now) : MeetingIcs.ForOccurrence(series, occurrence, now), title,
            occurrence?.StartAt ?? series.DtStart, occurrence is null ? series.Location : occurrence.Location, null, participants, guests, organizer, ct);
    }

    public async Task RsvpAnsweredAsync(MeetingSeries series, string title, Guid responderEmployeeId, bool accepted, CancellationToken ct)
    {
        if (responderEmployeeId == series.OrganizerEmployeeId)
            return;
        var people = await _employees.ListByIdsAsync(series.TenantId, [series.OrganizerEmployeeId, responderEmployeeId], ct);
        if (!people.TryGetValue(series.OrganizerEmployeeId, out var organizer) || organizer.UserId == Guid.Empty)
            return;
        var responder = people.TryGetValue(responderEmployeeId, out var r) ? $"{r.FirstName} {r.LastName}".Trim() : "Someone";
        await _sender.NotifyRsvpAnsweredAsync(series.TenantId, organizer.UserId, title, responder, accepted, ct);
    }

    public async Task LinkReadyAsync(MeetingSeries series, CalendarEventMeeting link, CancellationToken ct)
    {
        var (participants, guests) = await SeriesRecipientsAsync(series, row => CalendarEmailKeys.Link(link.Id, link.LinkGeneration, row), ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        await _sender.SendMeetingLinkAsync(series.TenantId, series.Title, series.DtStart, series.Location, participants, guests,
            await OrganizerNameAsync(series, ct), link.JoinUrl, MeetingIcs.ForSeries(series, _clock.UtcNow), ct);
    }

    public async Task LinkRemovedAsync(MeetingSeries series, CalendarEventMeeting link, CancellationToken ct)
    {
        var (participants, guests) = await SeriesRecipientsAsync(series, row => CalendarEmailKeys.LinkRemoved(link.Id, link.LinkGeneration, row), ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        await _sender.SendMeetingLinkRemovedAsync(series.TenantId, series.Title, series.DtStart, series.Location, participants, guests,
            await OrganizerNameAsync(series, ct), ct);
    }

    public async Task NotifyOrganizerAsync(MeetingSeries series, string noticeCode, string provider, CancellationToken ct)
    {
        var people = await _employees.ListByIdsAsync(series.TenantId, [series.OrganizerEmployeeId], ct);
        if (people.TryGetValue(series.OrganizerEmployeeId, out var organizer) && organizer.UserId != Guid.Empty)
            await _sender.NotifyOrganizerAboutMeetingAsync(series.TenantId, organizer.UserId, noticeCode, series.Title,
                CalendarMeetingProviderNames.Display(provider), ct);
    }

    private static string? JoinUrl(CalendarEventMeeting? link)
        => link is { Status: CalendarEventMeetingStatuses.Active, JoinUrl.Length: > 0 } ? link.JoinUrl : null;

    private async Task<string> OrganizerNameAsync(MeetingSeries series, CancellationToken ct)
    {
        var people = await _employees.ListByIdsAsync(series.TenantId, [series.OrganizerEmployeeId], ct);
        return people.TryGetValue(series.OrganizerEmployeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : "Someone";
    }

    private async Task<(IReadOnlyList<CalendarParticipantRecipient>, IReadOnlyList<CalendarGuestRecipient>)> SeriesRecipientsAsync(
        MeetingSeries series, Func<Guid, string> key, CancellationToken ct)
    {
        var participants = (await _meetings.ListSeriesParticipantsAsync(series.TenantId, series.Id, ct))
            .Where(p => !p.IsDeleted && p.EmployeeId != series.OrganizerEmployeeId)
            .Select(p => new CalendarParticipantRecipient(p.EmployeeId, key(p.Id))).ToList();
        var guests = (await _meetings.ListSeriesGuestsAsync(series.TenantId, series.Id, ct))
            .Select(g => new CalendarGuestRecipient(g.Email, key(g.Id))).ToList();
        return (participants, guests);
    }

    /// <summary>The occurrence's effective attendees (series − removes + adds), each keyed by the row that makes them one:
    /// their series row, or their per-occurrence "add" row.</summary>
    private async Task<(IReadOnlyList<CalendarParticipantRecipient>, IReadOnlyList<CalendarGuestRecipient>)> OccurrenceRecipientsAsync(
        MeetingSeries series, MeetingDetails occurrence, Func<Guid, string> key, CancellationToken ct)
    {
        var seriesParticipants = (await _meetings.ListSeriesParticipantsAsync(series.TenantId, series.Id, ct)).Where(p => !p.IsDeleted).ToList();
        var seriesGuests = await _meetings.ListSeriesGuestsAsync(series.TenantId, series.Id, ct);
        var overrides = await _meetings.ListOverridesAsync(series.TenantId, [occurrence.TaskId], ct);

        var removedEmployees = overrides.Where(o => o.Change == MeetingAttendeeChanges.Remove && o.EmployeeId is not null)
            .Select(o => o.EmployeeId!.Value).ToHashSet();
        var removedEmails = overrides.Where(o => o.Change == MeetingAttendeeChanges.Remove && o.Email is not null)
            .Select(o => o.Email!.ToLowerInvariant()).ToHashSet();
        var seriesEmployees = seriesParticipants.Select(p => p.EmployeeId).ToHashSet();
        var seriesEmails = seriesGuests.Select(g => g.Email.ToLowerInvariant()).ToHashSet();

        var participants = seriesParticipants
            .Where(p => p.EmployeeId != series.OrganizerEmployeeId && !removedEmployees.Contains(p.EmployeeId))
            .Select(p => new CalendarParticipantRecipient(p.EmployeeId, key(p.Id)))
            .Concat(overrides.Where(o => o.Change == MeetingAttendeeChanges.Add && o.EmployeeId is Guid id
                    && id != series.OrganizerEmployeeId && !seriesEmployees.Contains(id))
                .Select(o => new CalendarParticipantRecipient(o.EmployeeId!.Value, key(o.Id))))
            .ToList();
        var guests = seriesGuests.Where(g => !removedEmails.Contains(g.Email.ToLowerInvariant()))
            .Select(g => new CalendarGuestRecipient(g.Email, key(g.Id)))
            .Concat(overrides.Where(o => o.Change == MeetingAttendeeChanges.Add && o.AttendeeKind == MeetingAttendeeKinds.Guest
                    && o.Email is not null && !seriesEmails.Contains(o.Email.ToLowerInvariant()))
                .Select(o => new CalendarGuestRecipient(o.Email!, key(o.Id))))
            .ToList();
        return (participants, guests);
    }
}
