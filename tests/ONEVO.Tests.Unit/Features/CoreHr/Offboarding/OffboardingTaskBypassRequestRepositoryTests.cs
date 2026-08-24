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

public class OffboardingTaskBypassRequestRepositoryTests
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
    public async Task HasPendingForTaskAsync_TrueOnlyForPendingStatus()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        db.OffboardingTaskBypassRequests.Add(new OffboardingTaskBypassRequest
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeChecklistTaskId = taskId,
            OffboardingRecordId = Guid.NewGuid(), RequestedById = Guid.NewGuid(), ApproverId = Guid.NewGuid(),
            BypassReason = "x", Status = BypassRequestStatuses.Rejected,
        });
        await db.SaveChangesAsync();

        var repo = new EfOffboardingTaskBypassRequestRepository(db);
        (await repo.HasPendingForTaskAsync(tenantId, taskId)).Should().BeFalse();
    }

    [Fact]
    public async Task ListPendingByApproverAsync_ScopesToApproverAndPendingOnly()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        db.OffboardingTaskBypassRequests.AddRange(
            new OffboardingTaskBypassRequest { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeChecklistTaskId = Guid.NewGuid(), OffboardingRecordId = Guid.NewGuid(), RequestedById = Guid.NewGuid(), ApproverId = approverId, BypassReason = "a", Status = BypassRequestStatuses.Pending },
            new OffboardingTaskBypassRequest { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeChecklistTaskId = Guid.NewGuid(), OffboardingRecordId = Guid.NewGuid(), RequestedById = Guid.NewGuid(), ApproverId = approverId, BypassReason = "b", Status = BypassRequestStatuses.Approved },
            new OffboardingTaskBypassRequest { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeChecklistTaskId = Guid.NewGuid(), OffboardingRecordId = Guid.NewGuid(), RequestedById = Guid.NewGuid(), ApproverId = Guid.NewGuid(), BypassReason = "c", Status = BypassRequestStatuses.Pending });
        await db.SaveChangesAsync();

        var repo = new EfOffboardingTaskBypassRequestRepository(db);
        var result = await repo.ListPendingByApproverAsync(tenantId, approverId);

        result.Should().ContainSingle().Which.BypassReason.Should().Be("a");
    }
}
