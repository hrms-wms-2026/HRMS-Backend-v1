using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Notifications;

namespace ONEVO.Tests.Unit.Features.Monitoring;

public sealed class EfNotificationRepositoryCountTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    private Notification N(NotificationType type, string createdAtUtc, Guid? employeeId = null) =>
        new()
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = employeeId ?? _employeeId,
            Type = type, Title = "t", Message = "m", CreatedAt = DateTimeOffset.Parse(createdAtUtc)
        };

    [Fact]
    public async Task CountByType_CountsOnlyThatEmployeeAndTypeInsideTheHalfOpenWindow()
    {
        await using var db = BuildInMemoryDb();
        db.MonitoringNotifications.AddRange(
            N(NotificationType.OutsideWorkLocationAlert, "2026-08-01T00:00:00+00:00"),   // in (inclusive start)
            N(NotificationType.OutsideWorkLocationAlert, "2026-08-15T10:00:00+00:00"),   // in
            N(NotificationType.OutsideWorkLocationAlert, "2026-09-01T00:00:00+00:00"),   // out (exclusive end)
            N(NotificationType.OutsideWorkLocationAlert, "2026-07-31T23:59:59+00:00"),   // out (before)
            N(NotificationType.LongIdleAlert, "2026-08-10T00:00:00+00:00"),              // wrong type
            N(NotificationType.OutsideWorkLocationAlert, "2026-08-10T00:00:00+00:00", Guid.NewGuid())); // other employee
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var count = await new EfNotificationRepository(db).CountByTypeAsync(
            _tenantId, _employeeId, NotificationType.OutsideWorkLocationAlert,
            DateTimeOffset.Parse("2026-08-01T00:00:00+00:00"), DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"),
            CancellationToken.None);

        Assert.Equal(2, count);
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
