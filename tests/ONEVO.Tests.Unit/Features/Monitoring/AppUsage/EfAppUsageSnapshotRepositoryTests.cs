using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.AppUsage;

public class EfAppUsageSnapshotRepositoryTests
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
    public async Task GetByEmployeeDateAsync_FiltersToTheGivenUtcDayOnly_AndOrdersByCapturedAt()
    {
        await using var db = BuildDb();
        var inDay1 = new AppUsageSnapshot { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero), ProcessName = "b.exe" };
        var inDay2 = new AppUsageSnapshot { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero), ProcessName = "a.exe" };
        var outOfDay = new AppUsageSnapshot { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero), ProcessName = "c.exe" };
        var otherEmployee = new AppUsageSnapshot { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = Guid.NewGuid(), AgentDeviceId = Guid.NewGuid(), CapturedAt = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero), ProcessName = "d.exe" };
        db.AppUsageSnapshots.AddRange(inDay1, inDay2, outOfDay, otherEmployee);
        await db.SaveChangesAsync();

        var repo = new EfAppUsageSnapshotRepository(db);
        var result = await repo.GetByEmployeeDateAsync(TenantId, EmployeeId, Day, page: 1, pageSize: 100, CancellationToken.None);

        result.Select(r => r.ProcessName).Should().Equal("a.exe", "b.exe");
    }

    [Fact]
    public async Task GetTotalCountAsync_CountsOnlyMatchingTenantEmployeeAndDay()
    {
        await using var db = BuildDb();
        db.AppUsageSnapshots.AddRange(
            new AppUsageSnapshot { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero) },
            new AppUsageSnapshot { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();

        var repo = new EfAppUsageSnapshotRepository(db);
        var count = await repo.GetTotalCountAsync(TenantId, EmployeeId, Day, CancellationToken.None);

        count.Should().Be(1);
    }
}
