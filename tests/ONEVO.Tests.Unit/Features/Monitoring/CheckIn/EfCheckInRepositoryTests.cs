using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn;

public sealed class EfCheckInRepositoryTests
{
    [Fact]
    public async Task ListForUsersInRangeAsync_ReturnsOnlyCheckInsForGivenUsersWithinRange()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);

        db.EmployeeCheckIns.AddRange(
            NewCheckIn(tenantId, userA, from.AddHours(4)),
            NewCheckIn(tenantId, userB, from.AddHours(5)),
            NewCheckIn(tenantId, otherUser, from.AddHours(6)),
            NewCheckIn(tenantId, userA, from.AddDays(-1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfCheckInRepository(db);
        var results = await repo.ListForUsersInRangeAsync(tenantId, new[] { userA, userB }, from, to, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, checkIn => Assert.Contains(checkIn.UserId, new[] { userA, userB }));
    }

    [Fact]
    public async Task ListForUsersInRangeAsync_ReturnsEmpty_WhenNoUserIdsGiven()
    {
        await using var db = BuildInMemoryDb();
        var repo = new EfCheckInRepository(db);

        var results = await repo.ListForUsersInRangeAsync(
            Guid.NewGuid(), Array.Empty<Guid>(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ListForUsersInRangeAsync_IsTenantScoped()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);

        db.EmployeeCheckIns.Add(NewCheckIn(otherTenantId, userId, from.AddHours(4)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfCheckInRepository(db);
        var results = await repo.ListForUsersInRangeAsync(tenantId, new[] { userId }, from, to, CancellationToken.None);

        Assert.Empty(results);
    }

    private static EmployeeCheckIn NewCheckIn(Guid tenantId, Guid userId, DateTimeOffset checkedInAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        DeviceRegistrationId = Guid.NewGuid(),
        CheckedInAt = checkedInAt,
        CreatedAt = checkedInAt,
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
