using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EfCalendarEventRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTimeOffset RangeStart = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeEnd = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetInDateRangeForCallerAsync_ReturnsEventsCreatedByCaller_WithinRange()
    {
        // AuditableEntityInterceptor stamps CreatedById from ICurrentUser on every save,
        // overwriting whatever the entity object was constructed with - so distinct
        // "creators" require distinct SaveChanges calls under distinct current-user mocks,
        // not one AddRange with manually-set CreatedById values.
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        await using var db = BuildInMemoryDb(currentUser.Object);

        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        var inRange = MakeEvent(startDate: new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
        db.PersonalCalendarEvents.Add(inRange);
        await db.SaveChangesAsync();

        var outOfRange = MakeEvent(startDate: new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        db.PersonalCalendarEvents.Add(outOfRange);
        await db.SaveChangesAsync();

        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        var otherUsers = MakeEvent(startDate: new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero));
        db.PersonalCalendarEvents.Add(otherUsers);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetInDateRangeForCallerAsync(TenantId, UserId, EmployeeId, RangeStart, RangeEnd, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(inRange.Id, result[0].Id);
    }

    [Fact]
    public async Task GetInDateRangeForCallerAsync_ReturnsEventsWhereCallerIsParticipant()
    {
        await using var db = BuildInMemoryDb(new Mock<ICurrentUser>().Object);
        var event1 = MakeEvent(startDate: new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero));
        db.PersonalCalendarEvents.Add(event1);
        db.CalendarEventParticipants.Add(new CalendarEventParticipant
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EventId = event1.Id, EmployeeId = EmployeeId,
            ResponseStatus = CalendarEventParticipantStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetInDateRangeForCallerAsync(TenantId, Guid.NewGuid(), EmployeeId, RangeStart, RangeEnd, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(event1.Id, result[0].Id);
    }

    [Fact]
    public async Task GetInDateRangeForCallerAsync_ExcludesRecurringMastersAndCancellationMarkers()
    {
        await using var db = BuildInMemoryDb(new Mock<ICurrentUser>().Object);
        var master = MakeEvent(startDate: new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
        master.Recurrence = CalendarRecurrences.Weekly;
        master.RecurrenceRule = "FREQ=WEEKLY";
        db.PersonalCalendarEvents.Add(master);

        var cancelledChild = MakeEvent(startDate: new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero));
        cancelledChild.RecurrenceParentId = master.Id;
        cancelledChild.RecurrenceOriginalStart = cancelledChild.StartDate;
        cancelledChild.IsRecurrenceCancelled = true;
        db.PersonalCalendarEvents.Add(cancelledChild);

        var detachedChild = MakeEvent(startDate: new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero));
        detachedChild.RecurrenceParentId = master.Id;
        detachedChild.RecurrenceOriginalStart = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        db.PersonalCalendarEvents.Add(detachedChild);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetInDateRangeForCallerAsync(
            TenantId, master.CreatedById, EmployeeId, RangeStart, RangeEnd, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(detachedChild.Id, result[0].Id);
    }

    [Fact]
    public async Task GetRecurringMastersForCallerAsync_ReturnsOnlyMastersCreatedByCaller_StartingBeforeTo()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        await using var db = BuildInMemoryDb(currentUser.Object);

        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        var mine = MakeEvent(startDate: new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero));
        mine.Recurrence = CalendarRecurrences.Weekly;
        mine.RecurrenceRule = "FREQ=WEEKLY";
        db.PersonalCalendarEvents.Add(mine);
        await db.SaveChangesAsync();

        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        var someoneElses = MakeEvent(startDate: new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.Zero));
        someoneElses.Recurrence = CalendarRecurrences.Weekly;
        someoneElses.RecurrenceRule = "FREQ=WEEKLY";
        db.PersonalCalendarEvents.Add(someoneElses);

        var startsTooLate = MakeEvent(startDate: RangeEnd.AddDays(5));
        startsTooLate.Recurrence = CalendarRecurrences.Weekly;
        startsTooLate.RecurrenceRule = "FREQ=WEEKLY";
        db.PersonalCalendarEvents.Add(startsTooLate);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetRecurringMastersForCallerAsync(TenantId, UserId, EmployeeId, RangeEnd, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(mine.Id, result[0].Id);
    }

    [Fact]
    public async Task GetChildrenForMasterAsync_ReturnsDetachedAndCancelledChildren()
    {
        await using var db = BuildInMemoryDb(new Mock<ICurrentUser>().Object);
        var masterId = Guid.NewGuid();
        var child1 = MakeEvent(startDate: new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
        child1.RecurrenceParentId = masterId;
        var child2 = MakeEvent(startDate: new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero));
        child2.RecurrenceParentId = masterId;
        child2.IsRecurrenceCancelled = true;
        var unrelated = MakeEvent(startDate: new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero));
        db.PersonalCalendarEvents.AddRange(child1, child2, unrelated);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetChildrenForMasterAsync(TenantId, masterId, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == child1.Id);
        Assert.Contains(result, c => c.Id == child2.Id);
    }

    [Fact]
    public async Task GetTrackedChildByOriginalStartAsync_ReturnsMatchingChild_OrNull()
    {
        await using var db = BuildInMemoryDb(new Mock<ICurrentUser>().Object);
        var masterId = Guid.NewGuid();
        var originalStart = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        var child = MakeEvent(startDate: originalStart);
        child.RecurrenceParentId = masterId;
        child.RecurrenceOriginalStart = originalStart;
        db.PersonalCalendarEvents.Add(child);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);

        var found = await repository.GetTrackedChildByOriginalStartAsync(TenantId, masterId, originalStart, CancellationToken.None);
        var notFound = await repository.GetTrackedChildByOriginalStartAsync(TenantId, masterId, originalStart.AddDays(7), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(child.Id, found!.Id);
        Assert.Null(notFound);
    }

    [Fact]
    public async Task GetParticipantsForEventsAsync_ReturnsParticipantsGroupedByEventId()
    {
        await using var db = BuildInMemoryDb(new Mock<ICurrentUser>().Object);
        var event1 = Guid.NewGuid();
        var event2 = Guid.NewGuid();
        db.CalendarEventParticipants.AddRange(
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = event1, EmployeeId = Guid.NewGuid(), ResponseStatus = CalendarEventParticipantStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow },
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = event1, EmployeeId = Guid.NewGuid(), ResponseStatus = CalendarEventParticipantStatuses.Accepted, CreatedAt = DateTimeOffset.UtcNow },
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = event2, EmployeeId = Guid.NewGuid(), ResponseStatus = CalendarEventParticipantStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetParticipantsForEventsAsync(TenantId, [event1, event2], CancellationToken.None);

        Assert.Equal(2, result[event1].Count);
        Assert.Single(result[event2]);
    }

    private static CalendarEvent MakeEvent(DateTimeOffset startDate) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, Title = "Event", StartDate = startDate,
        EndDate = startDate.AddHours(1), SourceType = CalendarEventSourceTypes.Manual,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ApplicationDbContext BuildInMemoryDb(ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
