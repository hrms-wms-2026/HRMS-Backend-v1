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

    [Fact]
    public async Task GetOpenSessionsForTasksAsync_ReturnsOnlyTasksWithAnOpenSession()
    {
        var tenantId = Guid.NewGuid();
        var taskWithOpen = Guid.NewGuid();
        var taskWithClosedOnly = Guid.NewGuid();
        var taskWithNone = Guid.NewGuid();
        var openEmployeeId = Guid.NewGuid();
        var openClockInAt = DateTimeOffset.UtcNow;
        await using var db = BuildInMemoryDb();
        var repository = new EfTaskClockingSessionRepository(db);

        await repository.AddAsync(new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskWithOpen,
            EmployeeId = openEmployeeId, ClockInAt = openClockInAt
        });
        await repository.AddAsync(new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskWithClosedOnly,
            EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow.AddHours(-1),
            ClockOutAt = DateTimeOffset.UtcNow, DurationMinutes = 60
        });
        await db.SaveChangesAsync();

        var result = await repository.GetOpenSessionsForTasksAsync(
            tenantId, new[] { taskWithOpen, taskWithClosedOnly, taskWithNone });

        Assert.Single(result);
        Assert.Equal(openEmployeeId, result[taskWithOpen].EmployeeId);
        Assert.Equal(openClockInAt, result[taskWithOpen].ClockInAt);
        Assert.False(result.ContainsKey(taskWithClosedOnly));
        Assert.False(result.ContainsKey(taskWithNone));
    }

    [Fact]
    public async Task GetTotalClosedSessionMinutesForTasksAsync_SumsClosedSessionsAndExcludesOpenAndOtherTasks()
    {
        var tenantId = Guid.NewGuid();
        var taskWithSessions = Guid.NewGuid();
        var taskWithNone = Guid.NewGuid();
        await using var db = BuildInMemoryDb();
        var repository = new EfTaskClockingSessionRepository(db);

        await repository.AddAsync(new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskWithSessions,
            EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow.AddHours(-2),
            ClockOutAt = DateTimeOffset.UtcNow.AddHours(-1), DurationMinutes = 60
        });
        await repository.AddAsync(new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskWithSessions,
            EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            ClockOutAt = DateTimeOffset.UtcNow, DurationMinutes = 30
        });
        // An open session on the same task must not be counted yet - it has no DurationMinutes.
        await repository.AddAsync(new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskWithSessions,
            EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await repository.GetTotalClosedSessionMinutesForTasksAsync(
            tenantId, new[] { taskWithSessions, taskWithNone });

        Assert.Equal(90, result[taskWithSessions]);
        Assert.False(result.ContainsKey(taskWithNone));
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
