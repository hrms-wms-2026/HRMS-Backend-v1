using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.AddCalendarEventParticipants;
using ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;
using ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;
using ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventParticipant;
using ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;
using static ONEVO.Tests.Integration.Calendar.CalendarNotificationDb;

namespace ONEVO.Tests.Integration.Calendar;

public sealed class CalendarNotificationOutboxIntegrationTests
{
    [Fact]
    public async Task Update_OutboxRowsExistAfterTheCommandReturns_InAppAndUpdatedEmailForParticipantAndGuest()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([participant], ["vendor@example.com"]);

        await using (var s = h.Scope())
        {
            var result = await new UpdateCalendarEventCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork).Handle(
                new UpdateCalendarEventCommand(evt.Id, "Quarterly review (moved)", null, evt.StartDate.AddHours(1), evt.EndDate.AddHours(1),
                    false, null, null, null, CalendarRecurrences.None),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var rows = await h.OutboxAsync();
        Assert.Single(InApp(rows, "calendar_event_updated"));
        var emails = Emails(rows, CalendarEmailKinds.Updated).ToList();
        Assert.Equal(2, emails.Count);
        Assert.All(emails, e => Assert.StartsWith($"update:{evt.Id}:1:", e.DeduplicationKey));
    }

    [Fact]
    public async Task EditAllOccurrences_OutboxRowsExistAfterTheCommandReturns()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var series = await h.AddEventAsync([participant], ["vendor@example.com"], recurring: true);

        await using (var s = h.Scope())
        {
            var result = await new EditRecurringOccurrenceCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork).Handle(
                new EditRecurringOccurrenceCommand(series.Id, series.StartDate, RecurrenceEditScope.AllEvents, "Weekly review",
                    null, series.StartDate, series.EndDate, false, null, null, null),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var rows = await h.OutboxAsync();
        Assert.Single(InApp(rows, "calendar_event_updated"));
        Assert.Equal(2, Emails(rows, CalendarEmailKinds.Updated).Count());
    }

    [Fact]
    public async Task CancelOccurrenceTwice_EmailsEachRecipientOnce()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var series = await h.AddEventAsync([participant], ["vendor@example.com"], recurring: true);
        var occurrence = series.StartDate.AddDays(7);

        for (var i = 0; i < 2; i++)
        {
            await using var s = h.Scope();
            var result = await new CancelRecurringOccurrenceCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork)
                .Handle(new CancelRecurringOccurrenceCommand(series.Id, occurrence), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var cancelled = Emails(await h.OutboxAsync(), CalendarEmailKinds.Cancelled).ToList();
        Assert.Equal(2, cancelled.Count); // participant + guest, not 4
        Assert.All(cancelled, e => Assert.StartsWith($"cancel:{series.Id}:{occurrence.UtcTicks}:", e.DeduplicationKey));
    }

    [Fact]
    public async Task Delete_EmailsTheGuestToo()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([participant], ["vendor@example.com"]);

        await using (var s = h.Scope())
        {
            var clock = new ONEVO.Infrastructure.Identity.Time.SystemDateTimeProvider();
            var result = await new DeleteCalendarEventCommandHandler(
                    h.OrganizerUser, s.Events, s.Employees, s.Sender, new EfCalendarEventMeetingRepository(s.Db, clock),
                    new ONEVO.Infrastructure.Services.SharedPlatform.Outbox.OutboxWriter(s.Db, CalendarNotificationDb.Encryption, clock), s.UnitOfWork)
                .Handle(new DeleteCalendarEventCommand(evt.Id), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var cancelled = Emails(await h.OutboxAsync(), CalendarEmailKinds.Cancelled).ToList();
        Assert.Contains(cancelled, e => e.Payload.RootElement.GetProperty("ToEmail").GetString() == "vendor@example.com");
        Assert.Equal(2, cancelled.Count);
    }

    [Fact]
    public async Task Accept_OrganizerInAppRowExistsAfterTheCommandReturns()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([participant], []);

        await using (var s = h.Scope(new StubCurrentUser(h.TenantId, participant.UserId)))
        {
            var result = await new RespondToCalendarEventCommandHandler(
                    new StubCurrentUser(h.TenantId, participant.UserId), s.Events, s.Employees, s.UnitOfWork, s.Sender)
                .Handle(new RespondToCalendarEventCommand(evt.Id, "Accepted"), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var accepted = Assert.Single(InApp(await h.OutboxAsync(), "calendar_event_rsvp_accepted"));
        Assert.Equal(h.Organizer.UserId, accepted.Payload.RootElement.GetProperty("RecipientUserId").GetGuid());
    }

    [Fact]
    public async Task RemoveThenReAddParticipant_SendsARemovedEmailAndASecondInvite()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([], []);

        async Task AddAsync()
        {
            await using var s = h.Scope();
            var result = await new AddCalendarEventParticipantsCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork)
                .Handle(new AddCalendarEventParticipantsCommand(evt.Id, [participant.Id]), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await AddAsync();
        await using (var s = h.Scope())
        {
            var result = await new RemoveCalendarEventParticipantCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork)
                .Handle(new RemoveCalendarEventParticipantCommand(evt.Id, participant.Id), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }
        await AddAsync();

        var rows = await h.OutboxAsync();
        var invites = Emails(rows, CalendarEmailKinds.Invite).ToList();
        Assert.Equal(2, invites.Count);
        Assert.NotEqual(invites[0].DeduplicationKey, invites[1].DeduplicationKey);
        Assert.Single(Emails(rows, CalendarEmailKinds.Removed));
        Assert.Single(InApp(rows, "calendar_event_participant_removed"));
    }

    [Fact]
    public async Task TwoConcurrentUpdates_GetDistinctRevisions_SoBothEmailsAreSent()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var evt = await h.AddEventAsync([], ["vendor@example.com"]);

        async Task UpdateAsync(string title)
        {
            await using var s = h.Scope();
            var result = await new UpdateCalendarEventCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork).Handle(
                new UpdateCalendarEventCommand(evt.Id, title, null, evt.StartDate, evt.EndDate, false, null, null, null, CalendarRecurrences.None),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await Task.WhenAll(UpdateAsync("First"), UpdateAsync("Second"));

        var keys = Emails(await h.OutboxAsync(), CalendarEmailKinds.Updated).Select(e => e.DeduplicationKey!.Split(':')[2]).OrderBy(k => k).ToList();
        Assert.Equal(new[] { "1", "2" }, keys);
    }
}
