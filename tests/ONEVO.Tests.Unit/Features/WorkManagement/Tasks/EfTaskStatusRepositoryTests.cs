using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class EfTaskStatusRepositoryTests
{
    [Fact]
    public async Task GetByIdsForTenantAsync_ReturnsOnlyMatchingTenantAndIds()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var wanted = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = Guid.NewGuid(), Name = "Done", Category = "done", Color = "#16A34A", MarksTaskComplete = true, DisplayOrder = 0 };
        var unwanted = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = Guid.NewGuid(), Name = "Active", Category = "active", Color = "#2563EB", MarksTaskComplete = false, DisplayOrder = 1 };
        var otherTenant = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = otherTenantId, ProjectId = Guid.NewGuid(), Name = "Done", Category = "done", Color = "#16A34A", MarksTaskComplete = true, DisplayOrder = 0 };
        db.TaskStatuses.AddRange(wanted, unwanted, otherTenant);
        await db.SaveChangesAsync();
        var repo = new EfTaskStatusRepository(db);

        var result = await repo.GetByIdsForTenantAsync(tenantId, new[] { wanted.Id, otherTenant.Id }, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(wanted.Id, result[0].Id);
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
