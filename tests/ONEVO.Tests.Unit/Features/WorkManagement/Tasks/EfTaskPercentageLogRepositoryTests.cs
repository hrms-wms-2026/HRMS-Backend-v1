using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class EfTaskPercentageLogRepositoryTests
{
    [Fact]
    public async Task GetForTaskAsync_ReturnsLogsAcrossAllSources_OrderedByChangedAt()
    {
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = BuildInMemoryDb();
        var repository = new EfTaskPercentageLogRepository(db);
        var pushEntry = new TaskPercentageLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), PreviousPercent = 0, NewPercent = 40, Source = TaskPercentageLogSources.Push, ClockingSessionId = Guid.NewGuid(), ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var manualEntry = new TaskPercentageLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), PreviousPercent = 40, NewPercent = 20, Source = TaskPercentageLogSources.ManualEdit, ClockingSessionId = null, ChangedAt = DateTimeOffset.UtcNow };
        await repository.AddAsync(manualEntry);
        await repository.AddAsync(pushEntry);
        await db.SaveChangesAsync();

        var result = await repository.GetForTaskAsync(tenantId, taskId);

        Assert.Equal(2, result.Count);
        Assert.Equal(pushEntry.Id, result[0].Id);
        Assert.Equal(manualEntry.Id, result[1].Id);
        Assert.Null(result[1].ClockingSessionId);
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
