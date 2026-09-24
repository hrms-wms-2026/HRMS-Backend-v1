using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.EntityAssets;

public sealed class EfEntityAssetRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string OwnerType = "project";
    private const string AssetPurpose = "project_cover";

    private static EntityAsset Asset(
        Guid ownerId, Guid fileRecordId, Guid? tenantId = null, string ownerType = OwnerType,
        string assetPurpose = AssetPurpose, bool isPrimary = true) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId ?? TenantId,
        OwnerType = ownerType,
        OwnerId = ownerId,
        AssetPurpose = assetPurpose,
        FileRecordId = fileRecordId,
        IsPrimary = isPrimary,
        CreatedByType = "user"
    };

    [Fact]
    public async Task GetPrimaryFileIdsByOwnerAsync_ReturnsFileIdKeyedByOwnerId_ForMatchingRows()
    {
        await using var db = BuildInMemoryDb();
        var ownerId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        db.EntityAssets.Add(Asset(ownerId, fileId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEntityAssetRepository(db);

        var result = await repository.GetPrimaryFileIdsByOwnerAsync(
            TenantId, OwnerType, [ownerId], AssetPurpose, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(fileId, result[ownerId]);
    }

    [Fact]
    public async Task GetPrimaryFileIdsByOwnerAsync_IgnoresNonPrimaryRows()
    {
        await using var db = BuildInMemoryDb();
        var ownerId = Guid.NewGuid();
        db.EntityAssets.Add(Asset(ownerId, Guid.NewGuid(), isPrimary: false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEntityAssetRepository(db);

        var result = await repository.GetPrimaryFileIdsByOwnerAsync(
            TenantId, OwnerType, [ownerId], AssetPurpose, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPrimaryFileIdsByOwnerAsync_IgnoresRowsFromAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        var ownerId = Guid.NewGuid();
        db.EntityAssets.Add(Asset(ownerId, Guid.NewGuid(), tenantId: Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEntityAssetRepository(db);

        var result = await repository.GetPrimaryFileIdsByOwnerAsync(
            TenantId, OwnerType, [ownerId], AssetPurpose, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPrimaryFileIdsByOwnerAsync_IgnoresRowsWithADifferentAssetPurpose()
    {
        await using var db = BuildInMemoryDb();
        var ownerId = Guid.NewGuid();
        db.EntityAssets.Add(Asset(ownerId, Guid.NewGuid(), assetPurpose: "avatar"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEntityAssetRepository(db);

        var result = await repository.GetPrimaryFileIdsByOwnerAsync(
            TenantId, OwnerType, [ownerId], AssetPurpose, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPrimaryFileIdsByOwnerAsync_IgnoresOwnersNotInTheRequestedSet()
    {
        await using var db = BuildInMemoryDb();
        var requestedOwnerId = Guid.NewGuid();
        var unrelatedOwnerId = Guid.NewGuid();
        db.EntityAssets.Add(Asset(unrelatedOwnerId, Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEntityAssetRepository(db);

        var result = await repository.GetPrimaryFileIdsByOwnerAsync(
            TenantId, OwnerType, [requestedOwnerId], AssetPurpose, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPrimaryFileIdsByOwnerAsync_BatchesMultipleOwnerIdsInOneCall()
    {
        await using var db = BuildInMemoryDb();
        var ownerId1 = Guid.NewGuid();
        var ownerId2 = Guid.NewGuid();
        var fileId1 = Guid.NewGuid();
        var fileId2 = Guid.NewGuid();
        db.EntityAssets.AddRange(Asset(ownerId1, fileId1), Asset(ownerId2, fileId2));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEntityAssetRepository(db);

        var result = await repository.GetPrimaryFileIdsByOwnerAsync(
            TenantId, OwnerType, [ownerId1, ownerId2], AssetPurpose, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(fileId1, result[ownerId1]);
        Assert.Equal(fileId2, result[ownerId2]);
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
