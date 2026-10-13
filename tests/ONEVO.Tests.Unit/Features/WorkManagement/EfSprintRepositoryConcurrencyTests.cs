using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

/// <summary>
/// WM-02: proves Sprint.Status being a concurrency token actually rejects a stale concurrent
/// write at the EF/repository layer, and that EfSprintRepository translates EF's
/// DbUpdateConcurrencyException into the application-level ConcurrencyConflictException (the
/// same convention EfWorkAreaChangeRequestRepository already uses).
/// </summary>
public sealed class EfSprintRepositoryConcurrencyTests
{
    [Fact]
    public async Task SaveChangesAsync_SecondWriterWithStaleStatus_ThrowsConcurrencyConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sprintId = Guid.NewGuid();

        await using (var seedDb = BuildDb(dbName))
        {
            seedDb.Sprints.Add(new Sprint
            {
                Id = sprintId, TenantId = tenantId, ProjectId = projectId, Name = "S1",
                Status = SprintStatuses.Active, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await seedDb.SaveChangesAsync();
        }

        // Two independent DbContexts = two concurrent requests, each with its own tracked copy
        // loaded before either one writes - exactly the double-click/retry shape in WM-02.
        await using var dbA = BuildDb(dbName);
        await using var dbB = BuildDb(dbName);
        var repoA = new EfSprintRepository(dbA);
        var repoB = new EfSprintRepository(dbB);

        var sprintA = await repoA.GetTrackedByIdForTenantAsync(tenantId, sprintId, CancellationToken.None);
        var sprintB = await repoB.GetTrackedByIdForTenantAsync(tenantId, sprintId, CancellationToken.None);

        sprintA!.Status = SprintStatuses.Complete;
        sprintB!.Status = SprintStatuses.Complete;

        await repoA.SaveChangesAsync(); // Winner: its tracked original value still matches the store.

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repoB.SaveChangesAsync()); // Loser: stale.
    }

    private static ApplicationDbContext BuildDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
