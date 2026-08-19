using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class OffboardingRecordRepositoryTests
{
    private static ApplicationDbContext CreateContext()
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

    [Fact]
    public async Task GetOpenByEmployeeIdAsync_ReturnsInitiatedOrInProgress_NotCompleted()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.OffboardingRecords.AddRange(
            new OffboardingRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Status = OffboardingRecordStatuses.Completed },
            new OffboardingRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Status = OffboardingRecordStatuses.InProgress });
        await db.SaveChangesAsync();

        var repo = new EfOffboardingRecordRepository(db);
        var result = await repo.GetOpenByEmployeeIdAsync(tenantId, employeeId);

        result.Should().NotBeNull();
        result!.Status.Should().Be(OffboardingRecordStatuses.InProgress);
    }

    [Fact]
    public async Task GetLatestByEmployeeIdAsync_ReturnsMostRecentByCreatedAt_EvenIfCompleted()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.OffboardingRecords.AddRange(
            new OffboardingRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Status = OffboardingRecordStatuses.Cancelled, CreatedAt = DateTimeOffset.UtcNow.AddDays(-10) },
            new OffboardingRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Status = OffboardingRecordStatuses.Completed, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var repo = new EfOffboardingRecordRepository(db);
        var result = await repo.GetLatestByEmployeeIdAsync(tenantId, employeeId);

        result!.Status.Should().Be(OffboardingRecordStatuses.Completed);
    }
}
