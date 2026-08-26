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

public sealed class EfTaskClockingSessionRepositoryTests
{
    [Fact]
    public async Task GetOpenSessionForTaskAsync_ReturnsOnlyTheOpenSession_NotClosedOnes()
    {
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = BuildInMemoryDb();
        var repository = new EfTaskClockingSessionRepository(db);
        var closed = new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow.AddHours(-2), ClockOutAt = DateTimeOffset.UtcNow.AddHours(-1), DurationMinutes = 60 };
        var open = new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow };
        await repository.AddAsync(closed);
        await repository.AddAsync(open);
        await db.SaveChangesAsync();

        var result = await repository.GetOpenSessionForTaskAsync(tenantId, taskId);

        Assert.NotNull(result);
        Assert.Equal(open.Id, result!.Id);
    }

    [Fact]
    public async Task GetForTaskAsync_ReturnsAllSessionsOrderedByClockInAt()
    {
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = BuildInMemoryDb();
        var repository = new EfTaskClockingSessionRepository(db);
        var first = new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow.AddHours(-2), ClockOutAt = DateTimeOffset.UtcNow.AddHours(-1), DurationMinutes = 60 };
        var second = new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow };
        await repository.AddAsync(second);
        await repository.AddAsync(first);
        await db.SaveChangesAsync();

        var result = await repository.GetForTaskAsync(tenantId, taskId);

        Assert.Equal(2, result.Count);
        Assert.Equal(first.Id, result[0].Id);
        Assert.Equal(second.Id, result[1].Id);
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
