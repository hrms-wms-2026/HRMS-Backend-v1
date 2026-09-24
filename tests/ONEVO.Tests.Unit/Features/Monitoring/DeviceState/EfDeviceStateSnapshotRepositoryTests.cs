using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.DeviceState;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.DeviceState;

public class EfDeviceStateSnapshotRepositoryTests
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
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 17);

    [Fact]
    public async Task GetByEmployeeDateAsync_FiltersToTheGivenUtcDayOnly()
    {
        await using var db = BuildDb();
        db.DeviceStateSnapshots.AddRange(
            new DeviceStateSnapshot { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero), IdleSeconds = 10, IsIdle = false },
            new DeviceStateSnapshot { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero), IdleSeconds = 200, IsIdle = true });
        await db.SaveChangesAsync();

        var repo = new EfDeviceStateSnapshotRepository(db);
        var result = await repo.GetByEmployeeDateAsync(TenantId, EmployeeId, Day, page: 1, pageSize: 100, CancellationToken.None);

        result.Should().ContainSingle(r => r.IdleSeconds == 10);
    }

    [Fact]
    public async Task GetTotalCountAsync_CountsOnlyMatchingTenantEmployeeAndDay()
    {
        await using var db = BuildDb();
        db.DeviceStateSnapshots.Add(
            new DeviceStateSnapshot { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero), IdleSeconds = 0, IsIdle = false });
        await db.SaveChangesAsync();

        var repo = new EfDeviceStateSnapshotRepository(db);
        var count = await repo.GetTotalCountAsync(TenantId, EmployeeId, Day, CancellationToken.None);

        count.Should().Be(1);
    }
}
