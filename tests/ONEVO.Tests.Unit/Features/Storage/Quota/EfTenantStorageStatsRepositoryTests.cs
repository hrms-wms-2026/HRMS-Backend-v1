using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.Quota.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Storage.Quota;

namespace ONEVO.Tests.Unit.Features.Storage.Quota;

// NOTE: TryReserveBytesAsync / ReleaseReservedBytesAsync / CommitReservedToUsedAsync
// use raw PostgreSQL (ExecuteSqlInterpolatedAsync with ON CONFLICT / GREATEST) for
// atomicity. EF Core's InMemory provider does not execute ExecuteSqlInterpolatedAsync
// against real SQL semantics, so those methods are intentionally NOT unit-tested here.
// The existing PostgreSQL integration tests remain the authority for that atomic
// reserve/release/commit behavior. This file only covers GetByTenantIdAsync.
public sealed class EfTenantStorageStatsRepositoryTests
{
    [Fact]
    public async Task GetByTenantIdAsync_ReturnsMatchingTenantRow()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.TenantStorageStats.Add(CreateStats(tenantId, usedR2Bytes: 1024));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfTenantStorageStatsRepository(db);

        var result = await repository.GetByTenantIdAsync(tenantId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(1024, result.UsedR2Bytes);
    }

    [Fact]
    public async Task GetByTenantIdAsync_UnknownTenant_ReturnsNull()
    {
        await using var db = BuildInMemoryDb();
        db.TenantStorageStats.Add(CreateStats(Guid.NewGuid(), usedR2Bytes: 0));
        await db.SaveChangesAsync();

        var repository = new EfTenantStorageStatsRepository(db);

        var result = await repository.GetByTenantIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByTenantIdAsync_DoesNotTrackReturnedRow()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.TenantStorageStats.Add(CreateStats(tenantId, usedR2Bytes: 0));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfTenantStorageStatsRepository(db);

        var result = await repository.GetByTenantIdAsync(tenantId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(db.ChangeTracker.Entries<TenantStorageStats>());
    }

    [Fact]
    public async Task GetByTenantIdAsync_MultipleTenantRows_ReturnsOnlyTheRequestedTenant()
    {
        await using var db = BuildInMemoryDb();
        var targetTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.TenantStorageStats.AddRange(
            CreateStats(targetTenantId, usedR2Bytes: 2048),
            CreateStats(otherTenantId, usedR2Bytes: 4096));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfTenantStorageStatsRepository(db);

        var result = await repository.GetByTenantIdAsync(targetTenantId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(targetTenantId, result.TenantId);
        Assert.Equal(2048, result.UsedR2Bytes);
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(
            currentUser.Object,
            dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }

    private static TenantStorageStats CreateStats(Guid tenantId, long usedR2Bytes)
    {
        return new TenantStorageStats
        {
            TenantId = tenantId,
            UsedR2Bytes = usedR2Bytes,
            UsedDbBytes = 0,
            ReservedR2Bytes = 0,
            LastCalculatedAt = null,
            UpdatedAt = null
        };
    }
}
