using System.Linq;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfSprintRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetByProjectAsync_ReturnsSprintsFromProjectObjectivesOnly()
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
        var visibleSprint = MakeSprint(objective.Id, projectId);
        var hiddenSprint = MakeSprint(otherObjective.Id, otherProjectId);
        db.Sprints.AddRange(visibleSprint, hiddenSprint);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfSprintRepository(db);
        var result = await repository.GetByProjectAsync(TenantId, projectId, CancellationToken.None);

        var sprint = Assert.Single(result);
        Assert.Equal(visibleSprint.Id, sprint.Id);
    }

    [Fact]
    public async Task GetByProjectAsync_ReturnsSprintsInCreatedAtOrder_RegardlessOfInsertionOrder()
    {
        // D8 requires draft (dateless) sprints to trail in creation order. Without an explicit
        // ORDER BY, Postgres heap order (and thus API order) can drift after updates, so this
        // proves the repository itself enforces CreatedAt ordering rather than relying on
        // incidental storage order.
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var objective = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId, Title = "Visible",
            OwnerId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        db.Objectives.Add(objective);
        var older = MakeSprint(objective.Id, projectId);
        var newer = MakeSprint(objective.Id, projectId);
        // Insert the newer sprint first so DB/heap insertion order is the opposite of CreatedAt
        // order - if the repository relied on insertion order, this would come back reversed.
        db.Sprints.AddRange(newer, older);
        await db.SaveChangesAsync();
        // AuditableEntityInterceptor stamps CreatedAt from IDateTimeProvider on Added entities,
        // overwriting whatever we set beforehand (and the unmocked provider returns the same
        // value for both, in the same SaveChanges call). Set the explicit, distinct CreatedAt
        // values AFTER the insert and save again: the interceptor only stamps CreatedAt for
        // EntityState.Added, so this second save (State.Modified) persists our values untouched.
        older.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        newer.CreatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfSprintRepository(db);
        var result = await repository.GetByProjectAsync(TenantId, projectId, CancellationToken.None);

        Assert.Equal(new[] { older.Id, newer.Id }, result.Select(s => s.Id));
    }

    private static Sprint MakeSprint(Guid objectiveId, Guid projectId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId,
        Name = "Sprint", StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2026, 8, 31),
        Status = SprintStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
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
