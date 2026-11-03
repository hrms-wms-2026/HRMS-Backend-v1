using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Tests.Integration.Calendar;
using Xunit;
using static ONEVO.Tests.Integration.WorkManagement.Meetings.MeetingAttendanceWorld;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Plan C Task 10 (plan C1 P2-2 for task meetings): 15 minutes before a materialized occurrence starts, every effective
/// attendee who has not declined - the organizer included - gets one in-app reminder, logged against the occurrence task id
/// and its start, so a second run reminds nobody again.</summary>
public sealed class MeetingReminderIntegrationTests
{
    private static readonly DateTimeOffset BeforeFirst = Week(0).AddMinutes(-10);

    private static async Task<int> RemindAsync(World w, DateTimeOffset at)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(at));
        var reminders = new CalendarEventReminderService(new EfCalendarEventRepository(db), new ONEVO.Infrastructure.Services.Calendar.IcalNetRecurrenceExpander(),
            new ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository(db),
            new CalendarNotificationSender(s.Outbox, new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db)),
            s.UnitOfWork, s.Meetings);
        return await reminders.SendDueAsync(w.H.TenantId, at, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<CalendarEventReminderLog>> LogsAsync(World w)
    {
        await using var db = w.H.NewContext();
        return await db.CalendarEventRemindersSent.AsNoTracking().ToListAsync();
    }

    private static async Task<int> ReminderNoticesAsync(World w)
    {
        await using var db = w.H.NewContext();
        return (await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.Type == OutboxMessageTypes.WorkNotification).ToListAsync())
            .Count(m => CalendarNotificationDb.Encryption.Decrypt(m.EncryptedPayload).Contains("calendar_event_reminder"));
    }

    [Fact]
    public async Task TaskMeetingReminder_FiresOncePerOccurrenceAndAttendee()
    {
        var w = await SeedAsync();

        var first = await RemindAsync(w, BeforeFirst);
        var again = await RemindAsync(w, BeforeFirst.AddMinutes(2));

        Assert.Equal((2, 0), (first, again));
        var logs = await LogsAsync(w);
        Assert.Equal(new[] { w.Attendee, w.Organizer }.Order(), logs.Select(l => l.EmployeeId).Order());
        Assert.All(logs, l => Assert.Equal((w.Tasks[0], Week(0)), (l.EventId, l.OccurrenceStart)));
        Assert.Equal(2, await ReminderNoticesAsync(w));
    }

    [Fact]
    public async Task DeclinedAttendee_GetsNoReminder()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            await db.CalendarEventParticipants.Where(p => p.MeetingSeriesId == w.SeriesId && p.EmployeeId == w.Attendee)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.ResponseStatus, CalendarEventParticipantStatuses.Rejected));
        }

        await RemindAsync(w, BeforeFirst);

        Assert.Equal([w.Organizer], (await LogsAsync(w)).Select(l => l.EmployeeId));
    }

    [Fact]
    public async Task PerOccurrenceRemove_GetsNoReminder()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var removed = await new MeetingTestServices(db, new MeetingTestClock(Now)).Attendees.RemoveOccurrenceEmployeeAsync(
                w.H.TenantId, w.Organizer, w.OrganizerUser, w.SeriesId, Week(0), w.Attendee, CancellationToken.None);
            Assert.True(removed.IsSuccess, removed.Error);
        }

        await RemindAsync(w, BeforeFirst);
        await RemindAsync(w, Week(1).AddMinutes(-10));

        var logs = await LogsAsync(w);
        Assert.Equal([w.Organizer], logs.Where(l => l.EventId == w.Tasks[0]).Select(l => l.EmployeeId));
        Assert.Contains(logs, l => l.EventId == w.Tasks[1] && l.EmployeeId == w.Attendee);   // only that occurrence
    }

    [Fact]
    public async Task CancelledOccurrence_GetsNoReminder()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var cancelled = await new MeetingTestServices(db, new MeetingTestClock(Now)).Cancels.CancelAsync(
                w.H.TenantId, w.Organizer, w.OrganizerUser, w.SeriesId, Week(0), MeetingScopes.This, CancellationToken.None);
            Assert.True(cancelled.IsSuccess, cancelled.Error);
        }

        var sent = await RemindAsync(w, BeforeFirst);

        Assert.Equal(0, sent);
        Assert.Empty(await LogsAsync(w));
    }
}
