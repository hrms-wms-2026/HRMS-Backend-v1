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
        db.CalendarEvents.Add(inRange);
        await db.SaveChangesAsync();

        var outOfRange = MakeEvent(startDate: new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        db.CalendarEvents.Add(outOfRange);
        await db.SaveChangesAsync();

        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        var otherUsers = MakeEvent(startDate: new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero));
        db.CalendarEvents.Add(otherUsers);
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
        db.CalendarEvents.Add(event1);
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
