using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;

using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public sealed class EfPlatformAccessRepositoryRoleLookupTests
{
    [Fact]
    public async Task GetFirstRoleNamesByUserIdsAsync_ReturnsEarliestAssignedRolePerUser()
    {
        await using var db = BuildInMemoryDb();

        var user = new PlatformUser { Id = Guid.NewGuid(), Email = "u1@onevo.io", FullName = "U1" };
        var earlierRole = new PlatformRole { Id = Guid.NewGuid(), Name = "Platform Manager" };
        var laterRole = new PlatformRole { Id = Guid.NewGuid(), Name = "Read-Only Manager" };

        db.PlatformUsers.Add(user);
        db.PlatformRoles.AddRange(earlierRole, laterRole);
        db.PlatformUserRoles.AddRange(
            new PlatformUserRole
            {
                UserId = user.Id,
                RoleId = earlierRole.Id,
                AssignedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            new PlatformUserRole
            {
                UserId = user.Id,
                RoleId = laterRole.Id,
                AssignedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPlatformAccessRepository(db, BuildClock());

        var result = await repository.GetFirstRoleNamesByUserIdsAsync(new[] { user.Id }, CancellationToken.None);

        result.Should().ContainKey(user.Id).WhoseValue.Should().Be("Platform Manager");
    }

    [Fact]
    public async Task GetFirstRoleNamesByUserIdsAsync_UserWithNoRole_IsAbsentFromResult()
    {
        await using var db = BuildInMemoryDb();

        var user = new PlatformUser { Id = Guid.NewGuid(), Email = "u2@onevo.io", FullName = "U2" };
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPlatformAccessRepository(db, BuildClock());

        var result = await repository.GetFirstRoleNamesByUserIdsAsync(new[] { user.Id }, CancellationToken.None);

        result.Should().NotContainKey(user.Id);
    }

    [Fact]
    public async Task GetFirstRoleNamesByUserIdsAsync_EmptyInput_ReturnsEmptyDictionaryWithoutQuerying()
    {
        await using var db = BuildInMemoryDb();
        var repository = new EfPlatformAccessRepository(db, BuildClock());

        var result = await repository.GetFirstRoleNamesByUserIdsAsync(Array.Empty<Guid>(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    private static IDateTimeProvider BuildClock()
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero));
        return clock.Object;
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
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
