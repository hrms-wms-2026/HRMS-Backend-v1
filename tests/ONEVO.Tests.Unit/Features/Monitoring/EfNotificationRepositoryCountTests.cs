using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
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

    [Theory]
    [InlineData(AttendanceAlertOutcomes.Excused, 1)]
    [InlineData(AttendanceAlertOutcomes.ConvertedToLeave, 1)]
    [InlineData(AttendanceAlertOutcomes.Confirmed, 2)]
    [InlineData(null, 2)]
    public async Task CountListAndRecent_LeaveOutNotificationsWhoseAttendanceAlertWasExcused(string? outcome, int expected)
    {
        await using var db = BuildInMemoryDb();
        var excused = N(NotificationType.OutsideWorkLocationAlert, "2026-08-10T00:00:00+00:00");
        db.MonitoringNotifications.AddRange(excused, N(NotificationType.OutsideWorkLocationAlert, "2026-08-11T00:00:00+00:00"));
        db.AttendanceAlerts.Add(new AttendanceAlert
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, WorkDate = new DateOnly(2026, 8, 10),
            SignalKey = AttendanceAlertSignals.LocationViolations, SourceId = excused.Id,
            Status = outcome is null ? AttendanceAlertStatuses.Open : AttendanceAlertStatuses.Resolved, Outcome = outcome
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repo = new EfNotificationRepository(db);
        DateTimeOffset from = DateTimeOffset.Parse("2026-08-01T00:00:00+00:00"), to = DateTimeOffset.Parse("2026-09-01T00:00:00+00:00");

        Assert.Equal(expected, await repo.CountByTypeAsync(_tenantId, _employeeId, NotificationType.OutsideWorkLocationAlert, from, to, CancellationToken.None));
        Assert.Equal(expected, (await repo.ListByTypesAsync(_tenantId, _employeeId, [NotificationType.OutsideWorkLocationAlert], from, to, 50, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task RecentAlert_IsNotRaised_ByAnExcusedNotification()
    {
        await using var db = BuildInMemoryDb();
        var idle = N(NotificationType.LongIdleAlert, "2026-08-10T10:00:00+00:00");
        db.MonitoringNotifications.Add(idle);
        db.AttendanceAlerts.Add(new AttendanceAlert
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, WorkDate = new DateOnly(2026, 8, 10),
            SignalKey = AttendanceAlertSignals.IdleActivityAlerts, SourceId = idle.Id,
            Status = AttendanceAlertStatuses.Resolved, Outcome = AttendanceAlertOutcomes.Excused
        });
        await db.SaveChangesAsync();

        var ids = await new EfNotificationRepository(db).GetEmployeeIdsWithRecentAlertAsync(
            _tenantId, [_employeeId], NotificationType.LongIdleAlert, DateTimeOffset.Parse("2026-08-10T06:00:00+00:00"), CancellationToken.None);

        Assert.Empty(ids);
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
