using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

/// <summary>
/// created_by_id has a foreign key to users since task 39c, so the interceptor writes a tenant user's id or NULL,
/// never Guid.Empty and never a platform_users id.
/// </summary>
public sealed class AuditableEntityInterceptorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Signed_in_tenant_user_is_stamped()
    {
        var userId = Guid.NewGuid();

        var createdBy = await SaveOneAsync(SignedIn(userId), platformUserId: null);

        Assert.Equal(userId, createdBy);
    }

    [Fact]
    public async Task No_signed_in_user_writes_null_not_guid_empty()
    {
        var createdBy = await SaveOneAsync(new Mock<ICurrentUser>().Object, platformUserId: null);

        Assert.Null(createdBy);
    }

    [Fact]
    public async Task Signed_in_user_with_an_empty_id_writes_null()
    {
        var createdBy = await SaveOneAsync(SignedIn(Guid.Empty), platformUserId: null);

        Assert.Null(createdBy);
    }

    [Fact]
    public async Task Platform_admin_writes_null_because_the_id_is_not_a_users_row()
    {
        var adminId = Guid.NewGuid();

        var createdBy = await SaveOneAsync(SignedIn(adminId), platformUserId: adminId);

        Assert.Null(createdBy);
    }

    [Fact]
    public async Task Tenant_user_is_stamped_when_a_different_platform_identity_is_present()
    {
        var userId = Guid.NewGuid();

        var createdBy = await SaveOneAsync(SignedIn(userId), platformUserId: Guid.NewGuid());

        Assert.Equal(userId, createdBy);
    }

    private static ICurrentUser SignedIn(Guid userId)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(userId);
        return user.Object;
    }

    private static async Task<Guid?> SaveOneAsync(ICurrentUser currentUser, Guid? platformUserId)
    {
        var platform = new Mock<ICurrentPlatformUserContext>();
        platform.SetupGet(x => x.UserId).Returns(platformUserId);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser, clock.Object, platform.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);

        var start = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
        var row = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Event", StartDate = start, EndDate = start.AddHours(1),
            SourceType = CalendarEventSourceTypes.Manual,
            CreatedById = Guid.NewGuid(), // overwritten by the interceptor
        };
        db.PersonalCalendarEvents.Add(row);
        await db.SaveChangesAsync();
        return row.CreatedById;
    }
}
