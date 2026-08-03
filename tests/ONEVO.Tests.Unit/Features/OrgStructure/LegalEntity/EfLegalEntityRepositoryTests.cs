using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public sealed class EfLegalEntityRepositoryTests
{
    [Fact]
    public async Task ListByTenantAsync_ReturnsOnlyMatchingTenantRows_OrderedByName()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.LegalEntities.AddRange(
            CreateLegalEntity(tenantId, "Zeta Co"),
            CreateLegalEntity(tenantId, "Alpha Co"),
            CreateLegalEntity(otherTenantId, "Other Tenant Co"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var results = await repository.ListByTenantAsync(tenantId, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("Alpha Co", results[0].Name);
        Assert.Equal("Zeta Co", results[1].Name);
    }

    [Fact]
    public async Task ListByTenantAsync_DoesNotTrackReturnedRows()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.LegalEntities.Add(CreateLegalEntity(tenantId, "Alpha Co"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        await repository.ListByTenantAsync(tenantId, CancellationToken.None);

        Assert.Empty(db.ChangeTracker.Entries<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>());
    }

    [Fact]
    public async Task GetByIdForTenantAsync_ReturnsEntity_WhenTenantAndIdMatch()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entity = CreateLegalEntity(tenantId, "Alpha Co");
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var result = await repository.GetByIdForTenantAsync(tenantId, entity.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.Id);
    }

    [Fact]
    public async Task GetByIdForTenantAsync_ReturnsNull_WhenIdBelongsToAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var entity = CreateLegalEntity(otherTenantId, "Other Tenant Co");
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var result = await repository.GetByIdForTenantAsync(tenantId, entity.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Update_PersistsChangesToExistingRow()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entity = CreateLegalEntity(tenantId, "Alpha Co");
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);
        var fetched = await repository.GetByIdForTenantAsync(tenantId, entity.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        fetched.CompanyCode = "ACME";

        repository.Update(fetched);
        await repository.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var reloaded = await db.LegalEntities.AsNoTracking().FirstAsync(l => l.Id == entity.Id);
        Assert.Equal("ACME", reloaded.CompanyCode);
    }

    [Fact]
    public async Task CountActiveByTenantAsync_CountsOnlyActiveRowsForTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var active1 = CreateLegalEntity(tenantId, "Active One");
        var active2 = CreateLegalEntity(tenantId, "Active Two");
        var inactive = CreateLegalEntity(tenantId, "Inactive One");
        inactive.IsActive = false;
        db.LegalEntities.AddRange(active1, active2, inactive);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var count = await repository.CountActiveByTenantAsync(tenantId, CancellationToken.None);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task NameExistsForTenantAsync_ReturnsTrue_WhenNameAlreadyUsedInTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.LegalEntities.Add(CreateLegalEntity(tenantId, "Alpha Co"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var exists = await repository.NameExistsForTenantAsync(tenantId, "Alpha Co", null, CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task NameExistsForTenantAsync_ReturnsFalse_WhenSameNameOnlyExistsInAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.LegalEntities.Add(CreateLegalEntity(otherTenantId, "Alpha Co"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var exists = await repository.NameExistsForTenantAsync(tenantId, "Alpha Co", null, CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task NameExistsForTenantAsync_ExcludesGivenId_ForUpdateSelfCheck()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entity = CreateLegalEntity(tenantId, "Alpha Co");
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var exists = await repository.NameExistsForTenantAsync(tenantId, "Alpha Co", entity.Id, CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task CompanyCodeExistsForTenantAsync_ReturnsTrue_WhenCodeAlreadyUsedInTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entity = CreateLegalEntity(tenantId, "Alpha Co");
        entity.CompanyCode = "ACME";
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var exists = await repository.CompanyCodeExistsForTenantAsync(tenantId, "ACME", null, CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task RegistrationNumberExistsForTenantAsync_ReturnsTrue_WhenNumberAlreadyUsedInTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entity = CreateLegalEntity(tenantId, "Alpha Co");
        entity.RegistrationNumber = "PV-001";
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var exists = await repository.RegistrationNumberExistsForTenantAsync(
            tenantId, "PV-001", null, CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task ParentExistsForTenantAsync_ReturnsTrue_WhenParentBelongsToSameTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var parent = CreateLegalEntity(tenantId, "Parent Co");
        db.LegalEntities.Add(parent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var exists = await repository.ParentExistsForTenantAsync(tenantId, parent.Id, CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task ParentExistsForTenantAsync_ReturnsFalse_WhenParentBelongsToAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var parent = CreateLegalEntity(otherTenantId, "Parent Co");
        db.LegalEntities.Add(parent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var exists = await repository.ParentExistsForTenantAsync(tenantId, parent.Id, CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task HasChildrenAsync_ReturnsTrue_WhenAnotherRowPointsToItAsParent()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var parent = CreateLegalEntity(tenantId, "Parent Co");
        db.LegalEntities.Add(parent);
        await db.SaveChangesAsync();
        var child = CreateLegalEntity(tenantId, "Child Co");
        child.ParentLegalEntityId = parent.Id;
        db.LegalEntities.Add(child);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var hasChildren = await repository.HasChildrenAsync(tenantId, parent.Id, CancellationToken.None);

        Assert.True(hasChildren);
    }

    [Fact]
    public async Task HasChildrenAsync_ReturnsFalse_WhenNoRowPointsToItAsParent()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entity = CreateLegalEntity(tenantId, "Standalone Co");
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfLegalEntityRepository(db);

        var hasChildren = await repository.HasChildrenAsync(tenantId, entity.Id, CancellationToken.None);

        Assert.False(hasChildren);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingAddAndReturnsAffectedRowCount()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var repository = new EfLegalEntityRepository(db);
        var entity = CreateLegalEntity(tenantId, "Alpha Co");

        await repository.AddAsync(entity, CancellationToken.None);
        var affectedRows = await repository.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, affectedRows);
        Assert.Equal(1, await db.LegalEntities.CountAsync(l => l.TenantId == tenantId));
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

    private static ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity CreateLegalEntity(Guid tenantId, string name)
    {
        return new ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            CountryCode = "LKA",
            CurrencyCode = "LKR",
            IsActive = true,
            IsPrimary = false
        };
    }
}
