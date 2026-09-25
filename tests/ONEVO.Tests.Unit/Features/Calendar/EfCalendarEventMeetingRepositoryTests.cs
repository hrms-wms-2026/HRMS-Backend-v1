using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EfCalendarEventMeetingRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetTrackedByCalendarEventAsync_ReturnsMatchingMeeting()
    {
        await using var db = BuildInMemoryDb();
        var eventId = Guid.NewGuid();
        var meeting = MakeMeeting(eventId);
        db.CalendarEventMeetings.Add(meeting);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventMeetingRepository(db, Mock.Of<IDateTimeProvider>());
        var result = await repository.GetTrackedByCalendarEventAsync(TenantId, eventId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(meeting.Id, result!.Id);
    }

    [Fact]
    public async Task GetTrackedByCalendarEventAsync_NoMeeting_ReturnsNull()
    {
        await using var db = BuildInMemoryDb();

        var repository = new EfCalendarEventMeetingRepository(db, Mock.Of<IDateTimeProvider>());
        var result = await repository.GetTrackedByCalendarEventAsync(TenantId, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDueForAttendanceSyncAsync_ReturnsOnlyActiveUnsyncedMeetingsPastEndDate()
    {
        await using var db = BuildInMemoryDb();
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        // Due: active, never synced, its event already ended.
        var dueEvent = MakeCalendarEvent(now.AddHours(-2), now.AddHours(-1));
        var due = MakeMeeting(dueEvent.Id);

        // Already synced - should be excluded even though its event also ended.
        var syncedEvent = MakeCalendarEvent(now.AddHours(-2), now.AddHours(-1));
        var alreadySynced = MakeMeeting(syncedEvent.Id);
        alreadySynced.LastAttendanceSyncedAt = now.AddMinutes(-5);

        // Cancelled - should be excluded.
        var cancelledEvent = MakeCalendarEvent(now.AddHours(-2), now.AddHours(-1));
        var cancelled = MakeMeeting(cancelledEvent.Id);
        cancelled.Status = CalendarEventMeetingStatuses.Cancelled;

        // Event hasn't ended yet - should be excluded.
        var futureEvent = MakeCalendarEvent(now.AddHours(1), now.AddHours(2));
        var future = MakeMeeting(futureEvent.Id);

        db.PersonalCalendarEvents.AddRange(dueEvent, syncedEvent, cancelledEvent, futureEvent);
        db.CalendarEventMeetings.AddRange(due, alreadySynced, cancelled, future);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.SetupGet(d => d.UtcNow).Returns(now);

        var repository = new EfCalendarEventMeetingRepository(db, dateTimeProvider.Object);
        var result = await repository.GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.MicrosoftTeams, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(due.Id, result[0].Id);
    }

    [Fact]
    public async Task GetDueForAttendanceSyncAsync_FiltersByProvider()
    {
        await using var db = BuildInMemoryDb();
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        var teamsEvent = MakeCalendarEvent(now.AddHours(-2), now.AddHours(-1));
        var teamsMeeting = MakeMeeting(teamsEvent.Id, CalendarEventMeetingProviders.MicrosoftTeams);

        var zoomEvent = MakeCalendarEvent(now.AddHours(-2), now.AddHours(-1));
        var zoomMeeting = MakeMeeting(zoomEvent.Id, CalendarEventMeetingProviders.Zoom);

        db.PersonalCalendarEvents.AddRange(teamsEvent, zoomEvent);
        db.CalendarEventMeetings.AddRange(teamsMeeting, zoomMeeting);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.SetupGet(d => d.UtcNow).Returns(now);

        var repository = new EfCalendarEventMeetingRepository(db, dateTimeProvider.Object);
        var result = await repository.GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.Zoom, CancellationToken.None);

        var meeting = Assert.Single(result);
        Assert.Equal(zoomMeeting.Id, meeting.Id);
    }

    private static CalendarEventMeeting MakeMeeting(Guid calendarEventId, string provider = CalendarEventMeetingProviders.MicrosoftTeams) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = calendarEventId,
        ExternalCalendarConnectionId = Guid.NewGuid(), Provider = provider,
        ExternalMeetingId = "graph-meeting-1", JoinUrl = "https://teams.microsoft.com/l/meetup-join/abc",
        Status = CalendarEventMeetingStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
    };

    private static CalendarEvent MakeCalendarEvent(DateTimeOffset start, DateTimeOffset end) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, Title = "Sprint planning",
        StartDate = start, EndDate = end, SourceType = CalendarEventSourceTypes.Manual,
        Recurrence = "none", IsAllDay = false, CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ONEVO.Application.Common.ServiceInterfaces.ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
