using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Tests.Integration.Calendar;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Plan C Task 2: Plan B's write paths email attendees with keyed, .ics-carrying messages (spec 3, 5.2, 5.3);
/// notes after the meeting send nothing (moves and the generator never call the notifier); a retried change emails nobody twice.</summary>
public sealed class MeetingNotificationIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = First.AddDays(6);   // week 0 is past, week 1 is next

    private sealed record World(MyTeamDb H, MeetingSeries Series, MeetingPlacement At, Guid Organizer, Guid Attendee,
        CalendarEventParticipant AttendeeRow, CalendarEventGuest GuestRow, Guid Week0, Guid Week1);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var organizer = h.AddEmployee(db);
        var attendee = h.AddEmployee(db);
        var series = h.AddSeries(db, at with { OrganizerEmployeeId = organizer.Id }, First, "FREQ=WEEKLY");
        h.AddSeriesParticipant(db, series, organizer.Id, CalendarEventParticipantStatuses.Accepted);
        var row = h.AddSeriesParticipant(db, series, attendee.Id, CalendarEventParticipantStatuses.Accepted);
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = h.TenantId, MeetingSeriesId = series.Id, Email = "client@example.com", CreatedAt = Now };
        db.CalendarEventGuests.Add(guest);
        var (w0, _) = h.AddOccurrence(db, series, at, First);
        var (w1, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
        await db.SaveChangesAsync();
        return new World(h, series, at with { OrganizerEmployeeId = organizer.Id }, organizer.Id, attendee.Id, row, guest, w0.Id, w1.Id);
    }

    private static MeetingTestServices Services(ONEVO.Infrastructure.Persistence.ApplicationDbContext db) => new(db, new MeetingTestClock(Now));

    private static async Task<IReadOnlyList<(string Type, string? Key, JsonElement Payload)>> OutboxAsync(MyTeamDb h)
    {
        await using var db = h.NewContext();
        var rows = await db.Set<ONEVO.Domain.Features.SharedPlatform.Entities.OutboxMessage>().AsNoTracking()
            .Where(m => m.TenantId == h.TenantId).OrderBy(m => m.CreatedAt).ToListAsync();
        return rows.Select(m => (m.Type, m.DeduplicationKey, JsonDocument.Parse(CalendarNotificationDb.Encryption.Decrypt(m.EncryptedPayload)).RootElement)).ToList();
    }

    private static IReadOnlyList<string?> EmailKeys(IEnumerable<(string Type, string? Key, JsonElement Payload)> rows, string kind)
        => rows.Where(r => r.Type == ONEVO.Application.Common.ServiceInterfaces.OutboxMessageTypes.CalendarEventInviteEmail
                && r.Payload.GetProperty("Kind").GetString() == kind)
            .Select(r => r.Key).Order().ToList();

    private static async Task EditAsync(World w, DateTimeOffset at, EditMeetingRequest edit)
    {
        await using var db = w.H.NewContext();
        var result = await Services(db).Edits.EditAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, at, edit, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task DirectCreate_QueuesOneInvitePerAttendee_WithTheSeriesIcs_AndNoneForTheOrganizer()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid organizer, attendee;
        MeetingPlacement at;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            organizer = h.AddEmployee(db).Id;
            attendee = h.AddEmployee(db).Id;
            await db.SaveChangesAsync();
        }

        await using (var db = h.NewContext())
        {
            var s = Services(db);
            var created = await s.UnitOfWork.ExecuteInTransactionAsync(ct => s.Factory.CreateDirectAsync(h.TenantId, organizer, Guid.NewGuid(),
                new ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacement(at.ProjectId, at.ObjectiveId, IsOffice: false, Direct: true),
                new CreateMeetingRequest(at.ProjectId, at.ObjectiveId, "Kickoff", null, null, false,
                    Now.AddDays(2), 30, false, "UTC", "FREQ=WEEKLY", [attendee], ["client@example.com"], null),
                MeetingCreationPaths.Direct, null, ct));
            Assert.True(created.IsSuccess, created.Error);
            await db.SaveChangesAsync();
        }

        var outbox = await OutboxAsync(h);
        await using var check = h.NewContext();
        var series = await check.MeetingSeries.SingleAsync();
        var rows = await check.CalendarEventParticipants.Where(p => p.MeetingSeriesId == series.Id && p.EmployeeId == attendee).Select(p => p.Id).ToListAsync();
        var guest = await check.CalendarEventGuests.SingleAsync(g => g.MeetingSeriesId == series.Id);
        Assert.Equal(new[] { CalendarEmailKeys.Invite(rows.Single()), CalendarEmailKeys.Invite(guest.Id) }.Order(), EmailKeys(outbox, CalendarEventEmailKinds.Invite));
        var invite = outbox.First(r => r.Key == CalendarEmailKeys.Invite(guest.Id)).Payload;
        Assert.Equal((series.Id.ToString(), "FREQ=WEEKLY"), (invite.GetProperty("EventId").GetString(), invite.GetProperty("RecurrenceRule").GetString()));
    }

    [Fact]
    public async Task EditThis_EmailsTheOccurrencesAttendees_KeyedByTaskRevision()
    {
        var w = await SeedAsync();

        await EditAsync(w, First.AddDays(7), new EditMeetingRequest(MeetingScopes.This, 0, "Moved", null, null, null, First.AddDays(7).AddHours(2),
            null, null, null, null));

        var keys = EmailKeys(await OutboxAsync(w.H), CalendarEventEmailKinds.Updated);
        Assert.Equal(new[] { CalendarEmailKeys.Update(w.Week1, 1, w.AttendeeRow.Id), CalendarEmailKeys.Update(w.Week1, 1, w.GuestRow.Id) }.Order(), keys);
    }

    [Fact]
    public async Task EditAll_NonKeyFields_EmailsSeriesAttendees_KeyedBySeriesRevision()
    {
        var w = await SeedAsync();

        await EditAsync(w, First.AddDays(7), new EditMeetingRequest(MeetingScopes.All, 0, "Renamed", null, null, null, null, null, null, null, null));

        Assert.Equal(new[] { CalendarEmailKeys.Update(w.Series.Id, 1, w.AttendeeRow.Id), CalendarEmailKeys.Update(w.Series.Id, 1, w.GuestRow.Id) }.Order(),
            EmailKeys(await OutboxAsync(w.H), CalendarEventEmailKinds.Updated));
    }

    [Fact]
    public async Task ASplit_UpdatesTheShortenedOldSeries_AndSendsTheNewOne()
    {
        var w = await SeedAsync();

        await EditAsync(w, First.AddDays(7), new EditMeetingRequest(MeetingScopes.Following, 0, null, null, null, null, First.AddDays(7).AddHours(1),
            null, null, null, null));

        await using var check = w.H.NewContext();
        var target = await check.MeetingSeries.SingleAsync(s => s.SplitFromSeriesId == w.Series.Id);
        var newRow = await check.CalendarEventParticipants.SingleAsync(p => p.MeetingSeriesId == target.Id && p.EmployeeId == w.Attendee);
        var keys = EmailKeys(await OutboxAsync(w.H), CalendarEventEmailKinds.Updated);
        Assert.Contains(CalendarEmailKeys.Update(w.Series.Id, 1, w.AttendeeRow.Id), keys);
        Assert.Contains(CalendarEmailKeys.Update(target.Id, 0, newRow.Id), keys);
    }

    [Fact]
    public async Task NotesAfterTheMeeting_SendNoEmail()
    {
        var w = await SeedAsync();

        await EditAsync(w, First, new EditMeetingRequest(MeetingScopes.This, 0, null, "Minutes", null, null, null, null, null, null, null));

        Assert.Empty(await OutboxAsync(w.H));
    }

    [Fact]
    public async Task CancelOne_And_CancelAll_UseTheirKeys_AndRetryingSendsNothingNew()
    {
        var w = await SeedAsync();
        async Task CancelAsync(string scope)
        {
            await using var db = w.H.NewContext();
            var result = await Services(db).Cancels.CancelAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, First.AddDays(7), scope, CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }

        await CancelAsync(MeetingScopes.This);
        await CancelAsync(MeetingScopes.This);
        await CancelAsync(MeetingScopes.All);
        await CancelAsync(MeetingScopes.All);

        var outbox = await OutboxAsync(w.H);
        // Week 0 already took place, so "all" does not CANCEL the series UID - that would take last week's meeting out of
        // people's calendars too. It ends the series' rule now (an update), once per attendee, and a retry adds nothing.
        Assert.Equal(new[]
        {
            CalendarEmailKeys.Cancel(w.Series.Id, First.AddDays(7), w.AttendeeRow.Id), CalendarEmailKeys.Cancel(w.Series.Id, First.AddDays(7), w.GuestRow.Id),
        }.Order(), EmailKeys(outbox, CalendarEventEmailKinds.Cancelled));
        var ended = outbox.Where(r => r.Payload.TryGetProperty("Kind", out var k) && k.GetString() == CalendarEventEmailKinds.Updated
            && r.Payload.GetProperty("RecurrenceRule").GetString() is { } rule && rule.Contains("UNTIL=")).ToList();
        Assert.Equal(2, ended.Count);
    }

    [Fact]
    public async Task RemovingASeriesAttendee_EmailsThemARemoval_AndAddingAPersonInvitesThem()
    {
        var w = await SeedAsync();
        Guid newcomer;
        await using (var db = w.H.NewContext())
        {
            newcomer = w.H.AddEmployee(db).Id;
            await db.SaveChangesAsync();
        }

        await using (var db = w.H.NewContext())
        {
            var s = Services(db);
            Assert.True((await s.Attendees.RemoveSeriesEmployeeAsync(w.H.TenantId, w.Organizer, w.Series.Id, w.Attendee, CancellationToken.None)).IsSuccess);
            Assert.True((await s.Attendees.AddSeriesAttendeesAsync(w.H.TenantId, w.Organizer, w.Series.Id,
                new MeetingAttendeesRequest([newcomer], []), CancellationToken.None)).IsSuccess);
        }

        var outbox = await OutboxAsync(w.H);
        await using var check = w.H.NewContext();
        var newRow = await check.CalendarEventParticipants.SingleAsync(p => p.MeetingSeriesId == w.Series.Id && p.EmployeeId == newcomer);
        Assert.Equal([CalendarEmailKeys.Removed(w.Series.Id, null, w.AttendeeRow.Id, Now)], EmailKeys(outbox, CalendarEventEmailKinds.Removed));
        Assert.Equal([CalendarEmailKeys.Invite(newRow.Id)], EmailKeys(outbox, CalendarEventEmailKinds.Invite));
    }

    [Fact]
    public async Task ReAddingARemovedAttendee_InvitesThemAgain()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var s = Services(db);
            Assert.True((await s.Attendees.RemoveSeriesEmployeeAsync(w.H.TenantId, w.Organizer, w.Series.Id, w.Attendee, CancellationToken.None)).IsSuccess);
        }
        await using (var db = w.H.NewContext())
        {
            var s = Services(db);
            Assert.True((await s.Attendees.AddSeriesAttendeesAsync(w.H.TenantId, w.Organizer, w.Series.Id,
                new MeetingAttendeesRequest([w.Attendee], []), CancellationToken.None)).IsSuccess);
        }

        var invites = EmailKeys(await OutboxAsync(w.H), CalendarEventEmailKinds.Invite);
        Assert.Single(invites);
        Assert.StartsWith($"invite:{w.AttendeeRow.Id}:", invites[0]);
    }

    [Fact]
    public async Task ReAddingASeriesAttendeeToOneOccurrence_InvitesThemToThatOccurrence()
    {
        // Taken off next week's occurrence, then put back on it: they attend it again and get its invite.
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
            Assert.True((await Services(db).Attendees.RemoveOccurrenceEmployeeAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
                w.Series.Id, First.AddDays(7), w.Attendee, CancellationToken.None)).IsSuccess);
        var before = EmailKeys(await OutboxAsync(w.H), CalendarEventEmailKinds.Invite).Count;

        await using (var db = w.H.NewContext())
        {
            var added = await Services(db).Attendees.AddOccurrenceAttendeesAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(),
                w.Series.Id, First.AddDays(7), new MeetingAttendeesRequest([w.Attendee], []), CancellationToken.None);
            Assert.True(added.IsSuccess, added.Error);
            Assert.Equal(1, added.Value!.Added);
        }

        Assert.Equal(before + 1, EmailKeys(await OutboxAsync(w.H), CalendarEventEmailKinds.Invite).Count);
    }

    [Fact]
    public async Task Respond_NotifiesTheOrganizerInApp_OnlyWhenTheAnswerChanges()
    {
        var w = await SeedAsync();
        async Task RespondAsync(string status)
        {
            await using var db = w.H.NewContext();
            Assert.True((await Services(db).Attendees.RespondAsync(w.H.TenantId, w.Attendee, w.Series.Id,
                new RespondToMeetingRequest(status, null, null), CancellationToken.None)).IsSuccess);
        }

        await RespondAsync(CalendarEventParticipantStatuses.Rejected);
        await RespondAsync(CalendarEventParticipantStatuses.Rejected);

        var notices = (await OutboxAsync(w.H)).Where(r => r.Type == ONEVO.Application.Common.ServiceInterfaces.OutboxMessageTypes.WorkNotification
            && r.Payload.GetProperty("TemplateCode").GetString() == "calendar_event_rsvp_rejected").ToList();
        Assert.Single(notices);
    }
}
