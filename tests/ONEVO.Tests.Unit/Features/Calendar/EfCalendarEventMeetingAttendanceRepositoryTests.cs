using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EfCalendarEventMeetingAttendanceRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetByMeetingIdAsync_ReturnsOnlyThatMeetingsAttendances()
    {
        await using var db = BuildInMemoryDb();
        var meetingId1 = Guid.NewGuid();
        var meetingId2 = Guid.NewGuid();
        db.CalendarEventMeetingAttendances.AddRange(
            MakeAttendance(meetingId1), MakeAttendance(meetingId1), MakeAttendance(meetingId2));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventMeetingAttendanceRepository(db);
        var result = await repository.GetByMeetingIdAsync(TenantId, meetingId1, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal(meetingId1, a.CalendarEventMeetingId));
    }

    private static CalendarEventMeetingAttendance MakeAttendance(Guid meetingId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventMeetingId = meetingId,
        ExternalParticipantEmail = "person@example.com", JoinedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
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
