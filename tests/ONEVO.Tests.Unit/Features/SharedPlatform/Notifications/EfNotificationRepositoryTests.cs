using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public sealed class EfNotificationRepositoryTests
{
    [Fact]
    public async Task ExistsAsync_TrueOnlyForExactTenantRecipientTemplateAndRelatedEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var relatedEntityId = Guid.NewGuid();

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RecipientUserId = recipientUserId,
            TemplateCode = "attendance_late_clockin_daily_summary", Title = "t", Body = "b",
            RelatedEntityType = "attendance_late_daily_summary", RelatedEntityId = relatedEntityId,
            IsRead = false, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfNotificationRepository(db);

        Assert.True(await repository.ExistsAsync(
            tenantId, recipientUserId, "attendance_late_clockin_daily_summary",
            "attendance_late_daily_summary", relatedEntityId, CancellationToken.None));
        Assert.False(await repository.ExistsAsync(
            tenantId, recipientUserId, "attendance_late_clockin_daily_summary",
            "attendance_late_daily_summary", Guid.NewGuid(), CancellationToken.None));
        Assert.False(await repository.ExistsAsync(
            tenantId, Guid.NewGuid(), "attendance_late_clockin_daily_summary",
            "attendance_late_daily_summary", relatedEntityId, CancellationToken.None));
    }

    private static ApplicationDbContext BuildInMemoryDb()
        => NewDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApplicationDbContext NewDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTime.Object),
            new SoftDeleteInterceptor(dateTime.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
