using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;
using DomainTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfWorkTaskRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly DateOnly Cutoff = new(2026, 9, 3); // today (2026-08-27) + 7 days

    [Fact]
    public async Task AnyActiveByStatusIdAsync_SoftDeletedTaskStillReferencesStatus_ReturnsTrue()
    {
        await using var db = BuildInMemoryDb();
        db.WorkTasks.Add(MakeTask(TenantId, StatusId, isDeleted: true));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkTaskRepository(db);

        var result = await repository.AnyActiveByStatusIdAsync(TenantId, StatusId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task AnyActiveByStatusIdAsync_ReferenceFromAnotherTenant_ReturnsFalse()
    {
        await using var db = BuildInMemoryDb();
        db.WorkTasks.Add(MakeTask(Guid.NewGuid(), StatusId, isDeleted: true));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkTaskRepository(db);

        var result = await repository.AnyActiveByStatusIdAsync(TenantId, StatusId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GetMyActiveTasksAsync_ExcludesCompletedTasksNoDueDateAndOtherEmployees()
    {
        await using var db = BuildInMemoryDb();
        var incompleteStatus = MakeStatus(isComplete: false);
        var completeStatus = MakeStatus(isComplete: true);
        db.TaskStatuses.AddRange(incompleteStatus, completeStatus);
        db.Projects.Add(MakeProject());

        var overdueTask = MakeTask(incompleteStatus.Id, new DateOnly(2026, 8, 1)); // overdue, incomplete
        var completedOverdueTask = MakeTask(completeStatus.Id, new DateOnly(2026, 8, 1)); // overdue but done
        var noDueDateTask = MakeTask(incompleteStatus.Id, null);
        var farFutureTask = MakeTask(incompleteStatus.Id, new DateOnly(2026, 12, 1)); // beyond cutoff
        var otherEmployeeTask = MakeTask(incompleteStatus.Id, new DateOnly(2026, 8, 28));
        db.WorkTasks.AddRange(overdueTask, completedOverdueTask, noDueDateTask, farFutureTask, otherEmployeeTask);

        db.TaskAssignments.AddRange(
            MakeAssignment(overdueTask.Id, EmployeeId),
            MakeAssignment(completedOverdueTask.Id, EmployeeId),
            MakeAssignment(noDueDateTask.Id, EmployeeId),
            MakeAssignment(farFutureTask.Id, EmployeeId),
            MakeAssignment(otherEmployeeTask.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkTaskRepository(db);
        var result = await repository.GetMyActiveTasksAsync(TenantId, EmployeeId, Cutoff, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(overdueTask.Id, result[0].Id);
        Assert.Equal("Acme Website Redesign", result[0].ProjectName);
    }

    [Fact]
    public async Task GetMyActiveTasksAsync_IncludesTaskDueExactlyAtCutoff()
    {
        await using var db = BuildInMemoryDb();
        var status = MakeStatus(isComplete: false);
        db.TaskStatuses.Add(status);
        db.Projects.Add(MakeProject());

        var task = MakeTask(status.Id, Cutoff);
        db.WorkTasks.Add(task);
        db.TaskAssignments.Add(MakeAssignment(task.Id, EmployeeId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkTaskRepository(db);
        var result = await repository.GetMyActiveTasksAsync(TenantId, EmployeeId, Cutoff, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMyTaskProgressRowsAsync_ReturnsEveryAssignedTaskRegardlessOfDueDateAndExcludesOtherEmployees()
    {
        await using var db = BuildInMemoryDb();
        var incompleteStatus = MakeStatus(isComplete: false);
        var completeStatus = MakeStatus(isComplete: true);
        db.TaskStatuses.AddRange(incompleteStatus, completeStatus);
        db.Projects.Add(MakeProject());

        var noDueDateTask = MakeTask(incompleteStatus.Id, null);
        var farFutureTask = MakeTask(incompleteStatus.Id, new DateOnly(2026, 12, 1));
        var completedTask = MakeTask(completeStatus.Id, new DateOnly(2026, 8, 1));
        var otherEmployeeTask = MakeTask(incompleteStatus.Id, new DateOnly(2026, 8, 28));
        db.WorkTasks.AddRange(noDueDateTask, farFutureTask, completedTask, otherEmployeeTask);

        db.TaskAssignments.AddRange(
            MakeAssignment(noDueDateTask.Id, EmployeeId),
            MakeAssignment(farFutureTask.Id, EmployeeId),
            MakeAssignment(completedTask.Id, EmployeeId),
            MakeAssignment(otherEmployeeTask.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkTaskRepository(db);
        var result = await repository.GetMyTaskProgressRowsAsync(TenantId, EmployeeId, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.MarksTaskComplete);
        Assert.Contains(result, r => !r.MarksTaskComplete && r.DueDate is null);
        Assert.Contains(result, r => !r.MarksTaskComplete && r.DueDate == new DateOnly(2026, 12, 1));
    }

    private static DomainTaskStatus MakeStatus(bool isComplete) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProjectId = ProjectId,
        Name = isComplete ? "Done" : "In Progress",
        MarksTaskComplete = isComplete,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static Project MakeProject() => new()
    {
        Id = ProjectId,
        TenantId = TenantId,
        Name = "Acme Website Redesign",
        Identifier = "AWR",
        StartDate = new DateOnly(2026, 1, 1),
        TargetDate = new DateOnly(2026, 12, 31),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static TaskAssignment MakeAssignment(Guid taskId, Guid employeeId) => new()
    {
        Id = Guid.NewGuid(),
        TaskId = taskId,
        UserId = Guid.NewGuid(),
        EmployeeId = employeeId,
        AssignedById = Guid.NewGuid(),
        AssignedAt = DateTimeOffset.UtcNow
    };

    private static WorkTask MakeTask(Guid statusId, DateOnly? dueDate) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProjectId = ProjectId,
        ObjectiveId = Guid.NewGuid(),
        ShortId = $"T-{Guid.NewGuid():N}",
        Title = "Task",
        StatusId = statusId,
        DueDate = dueDate,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static WorkTask MakeTask(Guid tenantId, Guid statusId, bool isDeleted) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ProjectId = Guid.NewGuid(),
        ObjectiveId = Guid.NewGuid(),
        ShortId = $"T-{Guid.NewGuid():N}",
        Title = "Task",
        StatusId = statusId,
        IsDeleted = isDeleted,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
