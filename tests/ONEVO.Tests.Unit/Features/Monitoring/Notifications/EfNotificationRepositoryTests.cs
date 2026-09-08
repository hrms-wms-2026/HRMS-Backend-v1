using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Notifications;

public sealed class EfNotificationRepositoryTests
{
    [Fact]
    public async Task GetEmployeeIdsWithRecentAlertAsync_ReturnsOnlyMatchingTypeWithinWindow()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var recentIdle = Guid.NewGuid();
        var staleIdle = Guid.NewGuid();
        var wrongType = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.MonitoringNotifications.AddRange(
            NewNotification(tenantId, recentIdle, NotificationType.LongIdleAlert, now.AddMinutes(-5)),
            NewNotification(tenantId, staleIdle, NotificationType.LongIdleAlert, now.AddHours(-3)),
            NewNotification(tenantId, wrongType, NotificationType.BreakReminder, now.AddMinutes(-5)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfNotificationRepository(db);
        var result = await repo.GetEmployeeIdsWithRecentAlertAsync(
            tenantId, new[] { recentIdle, staleIdle, wrongType }, NotificationType.LongIdleAlert, now.AddHours(-1), CancellationToken.None);

        Assert.Single(result);
        Assert.Contains(recentIdle, result);
    }

    [Fact]
    public async Task GetEmployeeIdsWithRecentAlertAsync_ReturnsEmptySet_WhenNoEmployeeIdsGiven()
    {
        await using var db = BuildInMemoryDb();
        var repo = new EfNotificationRepository(db);

        var result = await repo.GetEmployeeIdsWithRecentAlertAsync(
            Guid.NewGuid(), Array.Empty<Guid>(), NotificationType.LongIdleAlert, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEmployeeIdsWithRecentAlertAsync_IsTenantScoped()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.MonitoringNotifications.Add(
            NewNotification(otherTenantId, employeeId, NotificationType.LongIdleAlert, now.AddMinutes(-5)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfNotificationRepository(db);
        var result = await repo.GetEmployeeIdsWithRecentAlertAsync(
            tenantId, new[] { employeeId }, NotificationType.LongIdleAlert, now.AddHours(-1), CancellationToken.None);

        Assert.Empty(result);
    }

    private static Notification NewNotification(Guid tenantId, Guid employeeId, NotificationType type, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        Type = type,
        Title = "Test",
        Message = "Test",
        CreatedAt = createdAt,
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }
}
