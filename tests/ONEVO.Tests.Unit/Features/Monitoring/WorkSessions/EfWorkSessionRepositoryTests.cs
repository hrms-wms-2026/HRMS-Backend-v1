using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.WorkSessions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.WorkSessions;

public class EfWorkSessionRepositoryTests
{
    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = new Mock<IDateTimeProvider>();
        var currentUser = new Mock<ICurrentUser>();
        var publisher = new Mock<IPublisher>();
        var tenant = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenant.Object);
    }

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 17);

    [Fact]
    public async Task GetForUserAndDateAsync_ReturnsOnlyMatchingTenantUserAndUtcDay()
    {
        await using var db = BuildDb();
        var inDay = Session(TenantId, UserId, new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero));
        var adjacentDay = Session(TenantId, UserId, new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero));
        var otherUser = Session(TenantId, Guid.NewGuid(), new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero));
        var otherTenant = Session(Guid.NewGuid(), UserId, new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero));
        db.EmployeeWorkSessions.AddRange(inDay, adjacentDay, otherUser, otherTenant);
        await db.SaveChangesAsync();

        var repo = new EfWorkSessionRepository(db);
        var result = await repo.GetForUserAndDateAsync(TenantId, UserId, Day, CancellationToken.None);

        result.Should().ContainSingle()
            .Which.Id.Should().Be(inDay.Id);
    }

    private static EmployeeWorkSession Session(Guid tenantId, Guid userId, DateTimeOffset clockInAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        DeviceRegistrationId = Guid.NewGuid(),
        ClockInAt = clockInAt,
        ClockOutAt = clockInAt.AddHours(8),
        AccumulatedBreakSeconds = 0,
        AccumulatedWorkSeconds = 28_800,
        BreakSessionCount = 0,
        ScheduleDisplay = "09:00-17:00",
        CreatedAt = clockInAt,
    };
}
