using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfLabelRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Label MakeLabel(Guid projectId, string name, Guid? tenantId = null, DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId ?? TenantId, ProjectId = projectId,
        Name = name, Color = "#2563EB", CreatedAt = createdAt ?? DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GetByProjectIdsAsync_ReturnsLabelsGroupedByProjectId()
    {
        await using var db = BuildInMemoryDb();
        var project1 = Guid.NewGuid();
        var project2 = Guid.NewGuid();
        db.Labels.AddRange(MakeLabel(project1, "Personal"), MakeLabel(project2, "Marketing"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLabelRepository(db);

        var result = await repository.GetByProjectIdsAsync(TenantId, [project1, project2], takePerProject: 5, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Personal", result[project1].Single().Name);
        Assert.Equal("Marketing", result[project2].Single().Name);
    }

    [Fact]
    public async Task GetByProjectIdsAsync_CapsResultsPerProjectAtTakePerProject()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow;
        db.Labels.AddRange(
            MakeLabel(projectId, "One", createdAt: baseTime),
            MakeLabel(projectId, "Two", createdAt: baseTime.AddMinutes(1)),
            MakeLabel(projectId, "Three", createdAt: baseTime.AddMinutes(2)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLabelRepository(db);

        var result = await repository.GetByProjectIdsAsync(TenantId, [projectId], takePerProject: 2, CancellationToken.None);

        Assert.Equal(2, result[projectId].Count);
        Assert.Equal(["One", "Two"], result[projectId].Select(l => l.Name));
    }

    [Fact]
    public async Task GetByProjectIdsAsync_IgnoresLabelsFromAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        db.Labels.Add(MakeLabel(projectId, "Other Tenant", tenantId: Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLabelRepository(db);

        var result = await repository.GetByProjectIdsAsync(TenantId, [projectId], takePerProject: 5, CancellationToken.None);

        Assert.False(result.ContainsKey(projectId));
    }

    [Fact]
    public async Task GetByProjectIdsAsync_ProjectWithNoLabels_IsAbsentFromResult()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();

        var repository = new EfLabelRepository(db);

        var result = await repository.GetByProjectIdsAsync(TenantId, [projectId], takePerProject: 5, CancellationToken.None);

        Assert.False(result.ContainsKey(projectId));
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, tenantContext.Object);
    }
}
