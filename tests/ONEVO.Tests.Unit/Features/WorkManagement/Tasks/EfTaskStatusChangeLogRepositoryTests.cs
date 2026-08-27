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

public sealed class EfTaskStatusChangeLogRepositoryTests
{
    [Fact]
    public async Task GetForTaskAsync_ReturnsOnlyThisTenantsLogsForThisTask_OrderedByChangedAt()
    {
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = BuildInMemoryDb();
        var repository = new EfTaskStatusChangeLogRepository(db);
        var older = new TaskStatusChangeLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), FromStatusId = Guid.NewGuid(), ToStatusId = Guid.NewGuid(), ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var newer = new TaskStatusChangeLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), FromStatusId = older.ToStatusId, ToStatusId = Guid.NewGuid(), ChangedAt = DateTimeOffset.UtcNow };
        await repository.AddAsync(newer);
        await repository.AddAsync(older);
        await db.SaveChangesAsync();

        var result = await repository.GetForTaskAsync(tenantId, taskId);

        Assert.Equal(2, result.Count);
        Assert.Equal(older.Id, result[0].Id);
        Assert.Equal(newer.Id, result[1].Id);
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
