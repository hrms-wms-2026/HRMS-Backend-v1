using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfWorkTaskRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();

    [Fact]
    public async Task GetByProjectAsync_ReturnsTasksFromProjectObjectivesOnly()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var objective = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId, Title = "Visible",
            OwnerId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        var otherObjective = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = otherProjectId, Title = "Hidden",
            OwnerId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        db.Objectives.AddRange(objective, otherObjective);
        var visibleTask = MakeProjectTask(TenantId, projectId, objective.Id);
        var hiddenTask = MakeProjectTask(TenantId, otherProjectId, otherObjective.Id);
        db.WorkTasks.AddRange(visibleTask, hiddenTask);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfWorkTaskRepository(db);
        var result = await repository.GetByProjectAsync(TenantId, projectId, CancellationToken.None);

        var task = Assert.Single(result);
        Assert.Equal(visibleTask.Id, task.Id);
    }

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

    private static WorkTask MakeProjectTask(Guid tenantId, Guid projectId, Guid objectiveId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId,
        ShortId = $"T-{Guid.NewGuid():N}", Title = "Task", StatusId = StatusId, CreatedAt = DateTimeOffset.UtcNow
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
