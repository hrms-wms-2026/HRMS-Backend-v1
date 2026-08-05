using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using Xunit;

using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class EfPositionRepositoryTests
{
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

    [Fact]
    public void PositionModel_ConfiguresNullableTransitionalFields_AndRestrictDeleteBehaviors()
    {
        using var db = BuildInMemoryDb();
        var entityType = db.Model.FindEntityType(typeof(PositionEntity));

        Assert.NotNull(entityType);

        // Primary key
        var pk = entityType.FindPrimaryKey();
        Assert.NotNull(pk);
        Assert.Single(pk.Properties);
        Assert.Equal("Id", pk.Properties[0].Name);

        // Required properties
        Assert.False(entityType.FindProperty(nameof(PositionEntity.TenantId))!.IsNullable);
        Assert.False(entityType.FindProperty(nameof(PositionEntity.Name))!.IsNullable);
        Assert.False(entityType.FindProperty(nameof(PositionEntity.PositionType))!.IsNullable);
        Assert.False(entityType.FindProperty(nameof(PositionEntity.MaxOccupancy))!.IsNullable);
        Assert.False(entityType.FindProperty(nameof(PositionEntity.IsActive))!.IsNullable);

        // Transitional nullable properties
        Assert.True(entityType.FindProperty(nameof(PositionEntity.LegalEntityId))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(PositionEntity.DepartmentId))!.IsNullable);

        // Foreign key delete behaviors
        var legalEntityFk = entityType.GetForeignKeys()
            .FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(LegalEntityEntity));
        Assert.NotNull(legalEntityFk);
        Assert.Equal(DeleteBehavior.Restrict, legalEntityFk.DeleteBehavior);

        var departmentFk = entityType.GetForeignKeys()
            .FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(DepartmentEntity));
        Assert.NotNull(departmentFk);
        Assert.Equal(DeleteBehavior.Restrict, departmentFk.DeleteBehavior);

        var selfFk = entityType.GetForeignKeys()
            .FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(PositionEntity));
        Assert.NotNull(selfFk);
        Assert.Equal(DeleteBehavior.Restrict, selfFk.DeleteBehavior);
    }

    [Fact]
    public void PositionReportingHistoryModel_ConfiguresForeignKeys_WithRestrictDeleteBehavior()
    {
        using var db = BuildInMemoryDb();
        var entityType = db.Model.FindEntityType(typeof(PositionReportingHistory));

        Assert.NotNull(entityType);

        var fks = entityType.GetForeignKeys().ToList();
        Assert.Equal(2, fks.Count);
        Assert.All(fks, fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    [Fact]
    public void ManagementCoverageRecordModel_ConfiguresForeignKeys_WithRestrictDeleteBehavior()
    {
        using var db = BuildInMemoryDb();
        var entityType = db.Model.FindEntityType(typeof(ManagementCoverageRecord));

        Assert.NotNull(entityType);

        var fks = entityType.GetForeignKeys().ToList();
        Assert.Equal(4, fks.Count);
        Assert.All(fks, fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    [Fact]
    public async Task AddAsync_And_GetByIdAsync_ReturnsPosition_ForSameTenantAndLegalEntity()
    {
        using var db = BuildInMemoryDb();
        var repository = new EfPositionRepository(db);

        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        var position = new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            DepartmentId = departmentId,
            Name = "Software Engineer",
            Code = "SE-01",
            PositionType = "unique",
            MaxOccupancy = 1,
            IsActive = true
        };

        await repository.AddAsync(position);
        await repository.SaveChangesAsync();

        var fetched = await repository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, position.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Software Engineer", fetched.Name);
        Assert.Equal("SE-01", fetched.Code);
    }

    [Fact]
    public async Task GetByIdForLegalEntityAsync_Excludes_OrphanLegacyPositions_WithNullLegalEntityId()
    {
        using var db = BuildInMemoryDb();
        var repository = new EfPositionRepository(db);

        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();

        var legacyOrphanPosition = new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = null,
            DepartmentId = null,
            Name = "Legacy Stub Position"
        };

        await repository.AddAsync(legacyOrphanPosition);
        await repository.SaveChangesAsync();

        var fetched = await repository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, legacyOrphanPosition.Id);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task ListByLegalEntityAsync_Excludes_OrphanLegacyPositions()
    {
        using var db = BuildInMemoryDb();
        var repository = new EfPositionRepository(db);

        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        var validPosition = new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            DepartmentId = deptId,
            Name = "Dev Lead",
            IsActive = true
        };

        var orphanPosition = new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = null,
            DepartmentId = null,
            Name = "Orphan Stub",
            IsActive = true
        };

        await repository.AddAsync(validPosition);
        await repository.AddAsync(orphanPosition);
        await repository.SaveChangesAsync();

        var list = await repository.ListByLegalEntityAsync(tenantId, legalEntityId, includeInactive: true);
        Assert.Single(list);
        Assert.Equal("Dev Lead", list[0].Name);
    }

    [Fact]
    public async Task ExistsByCodeAsync_CaseInsensitive_And_IgnoresOrphans()
    {
        using var db = BuildInMemoryDb();
        var repository = new EfPositionRepository(db);

        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();

        var validPosition = new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            DepartmentId = Guid.NewGuid(),
            Name = "Finance Manager",
            Code = "FIN-MGR"
        };

        var orphanPosition = new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = null,
            Name = "Orphan Fin",
            Code = "FIN-MGR"
        };

        await repository.AddAsync(validPosition);
        await repository.AddAsync(orphanPosition);
        await repository.SaveChangesAsync();

        Assert.True(await repository.ExistsByCodeAsync(tenantId, legalEntityId, "fin-mgr"));
        Assert.True(await repository.ExistsByNameAsync(tenantId, legalEntityId, "Finance Manager"));
        Assert.False(await repository.ExistsByCodeAsync(tenantId, legalEntityId, "fin-mgr", excludingPositionId: validPosition.Id));
    }

    [Fact]
    public async Task CountActiveByDepartmentAsync_And_ReportsTo_Count_WorkCorrectly()
    {
        using var db = BuildInMemoryDb();
        var repository = new EfPositionRepository(db);

        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var rootPosId = Guid.NewGuid();

        var rootPos = new PositionEntity
        {
            Id = rootPosId,
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            DepartmentId = departmentId,
            Name = "VP Eng",
            IsActive = true
        };

        var childPos = new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            DepartmentId = departmentId,
            Name = "Director Eng",
            ReportsToPositionId = rootPosId,
            IsActive = true
        };

        await repository.AddAsync(rootPos);
        await repository.AddAsync(childPos);
        await repository.SaveChangesAsync();

        var deptCount = await repository.CountActiveByDepartmentAsync(tenantId, legalEntityId, departmentId);
        Assert.Equal(2, deptCount);

        var reportsCount = await repository.CountActiveReportsToPositionAsync(tenantId, legalEntityId, rootPosId);
        Assert.Equal(1, reportsCount);
    }

    [Fact]
    public async Task ListPageAsync_FiltersByTenantAndLegalEntity_AndReturnsPaginationMetadata()
    {
        using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Positions.AddRange(
            CreatePosition(tenantId, legalEntityId, "Alpha", "ALPHA"),
            CreatePosition(tenantId, legalEntityId, "Beta", "BETA"),
            CreatePosition(tenantId, otherLegalEntityId, "Other LE", "OTHERLE"),
            CreatePosition(otherTenantId, legalEntityId, "Other Tenant", "OTHERTEN"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var page = await repository.ListPageAsync(
            tenantId, legalEntityId, departmentId: null, search: null, includeInactive: false,
            sortBy: "name", sortDirection: "asc", page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal("Alpha", page.Items[0].Name);
        Assert.Equal("Beta", page.Items[1].Name);
    }

    [Fact]
    public async Task ListPageAsync_SkipsAndTakesForSecondPage()
    {
        using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            db.Positions.Add(CreatePosition(tenantId, legalEntityId, $"Position {i:00}", $"POS{i:00}"));
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var page = await repository.ListPageAsync(
            tenantId, legalEntityId, departmentId: null, search: null, includeInactive: false,
            sortBy: "name", sortDirection: "asc", page: 2, pageSize: 2, CancellationToken.None);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal("Position 02", page.Items[0].Name);
        Assert.Equal("Position 03", page.Items[1].Name);
    }

    [Fact]
    public async Task ListPageAsync_FiltersInactiveRowsUnlessIncluded()
    {
        using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var inactive = CreatePosition(tenantId, legalEntityId, "Retired", "RETIRED");
        inactive.IsActive = false;
        db.Positions.AddRange(CreatePosition(tenantId, legalEntityId, "Active", "ACTIVE"), inactive);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var activeOnly = await repository.ListPageAsync(
            tenantId, legalEntityId, null, null, includeInactive: false,
            "name", "asc", 1, 10, CancellationToken.None);
        var allRows = await repository.ListPageAsync(
            tenantId, legalEntityId, null, null, includeInactive: true,
            "name", "asc", 1, 10, CancellationToken.None);

        Assert.Single(activeOnly.Items);
        Assert.Equal(2, allRows.TotalCount);
    }

    [Fact]
    public async Task ListPageAsync_FiltersBySearchAcrossNameAndCode()
    {
        using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Positions.AddRange(
            CreatePosition(tenantId, legalEntityId, "Finance Manager", "FIN-MGR"),
            CreatePosition(tenantId, legalEntityId, "Sales Lead", "SALES-LEAD"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var byName = await repository.ListPageAsync(
            tenantId, legalEntityId, null, "finance", false, "name", "asc", 1, 10, CancellationToken.None);
        var byCode = await repository.ListPageAsync(
            tenantId, legalEntityId, null, "sales-lead", false, "name", "asc", 1, 10, CancellationToken.None);

        Assert.Single(byName.Items);
        Assert.Equal("Finance Manager", byName.Items[0].Name);
        Assert.Single(byCode.Items);
        Assert.Equal("Sales Lead", byCode.Items[0].Name);
    }

    [Fact]
    public async Task ListPageAsync_FiltersByDepartmentId()
    {
        using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var inDept = CreatePosition(tenantId, legalEntityId, "In Dept", "INDEPT");
        inDept.DepartmentId = departmentId;
        db.Positions.AddRange(inDept, CreatePosition(tenantId, legalEntityId, "No Dept", "NODEPT"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var page = await repository.ListPageAsync(
            tenantId, legalEntityId, departmentId, null, false, "name", "asc", 1, 10, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal("In Dept", page.Items[0].Name);
    }

    [Fact]
    public async Task CountHeadDepartmentReferencesAsync_CountsDepartmentsReferencingThisPositionAsHead()
    {
        using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var position = CreatePosition(tenantId, legalEntityId, "Head Candidate", "HEAD");
        db.Positions.Add(position);
        db.Departments.Add(new DepartmentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = "Operations",
            HeadPositionId = position.Id,
            IsActive = true
        });
        db.Departments.Add(new DepartmentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = "Sales",
            HeadPositionId = null,
            IsActive = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var count = await repository.CountHeadDepartmentReferencesAsync(
            tenantId, legalEntityId, position.Id, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CountHeadDepartmentReferencesAsync_ReturnsZero_WhenNoDepartmentReferencesPosition()
    {
        using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var position = CreatePosition(tenantId, legalEntityId, "Not A Head", "NOTHEAD");
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var count = await repository.CountHeadDepartmentReferencesAsync(
            tenantId, legalEntityId, position.Id, CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ListByLegalEntityAsync_ExcludesOtherTenantAndOtherLegalEntityPositions()
    {
        // GetPositionTreeQueryHandler builds the tree directly from this method's output, so
        // proving cross-tenant/cross-legal-entity exclusion here is what actually discharges
        // "tree excludes cross-company positions" - a handler test with a hand-built mock list
        // cannot prove the repository-level filter is correct.
        using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Positions.AddRange(
            CreatePosition(tenantId, legalEntityId, "In Scope", "INSCOPE"),
            CreatePosition(tenantId, otherLegalEntityId, "Other Legal Entity", "OTHERLE2"),
            CreatePosition(otherTenantId, legalEntityId, "Other Tenant", "OTHERTEN2"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var results = await repository.ListByLegalEntityAsync(
            tenantId, legalEntityId, includeInactive: false, departmentId: null, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("In Scope", results[0].Name);
    }

    private static PositionEntity CreatePosition(Guid tenantId, Guid legalEntityId, string name, string code)
    {
        return new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = name,
            Code = code,
            IsActive = true
        };
    }
}
