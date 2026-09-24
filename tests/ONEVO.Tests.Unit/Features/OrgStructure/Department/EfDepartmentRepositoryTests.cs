using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Department;

public sealed class EfDepartmentRepositoryTests
{
    [Fact]
    public async Task ListByLegalEntityAsync_ReturnsOnlyMatchingTenantAndLegalEntityRows_OrderedByName()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Departments.AddRange(
            CreateDepartment(tenantId, legalEntityId, "Zeta"),
            CreateDepartment(tenantId, legalEntityId, "Alpha"),
            CreateDepartment(tenantId, otherLegalEntityId, "Other Legal Entity Dept"),
            CreateDepartment(otherTenantId, legalEntityId, "Other Tenant Dept"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var results = await repository.ListByLegalEntityAsync(
            tenantId, legalEntityId, includeInactive: false, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("Alpha", results[0].Name);
        Assert.Equal("Zeta", results[1].Name);
    }

    [Fact]
    public async Task ListByLegalEntityAsync_DoesNotTrackReturnedRows()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.Add(CreateDepartment(tenantId, legalEntityId, "Alpha"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        await repository.ListByLegalEntityAsync(
            tenantId, legalEntityId, includeInactive: false, CancellationToken.None);

        Assert.Empty(db.ChangeTracker.Entries<ONEVO.Domain.Features.OrgStructure.Entities.Department>());
    }

    [Fact]
    public async Task ListByLegalEntityAsync_FiltersInactiveRowsUnlessIncluded()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var inactive = CreateDepartment(tenantId, legalEntityId, "Inactive");
        inactive.IsActive = false;
        db.Departments.AddRange(
            CreateDepartment(tenantId, legalEntityId, "Active"),
            inactive);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var activeOnly = await repository.ListByLegalEntityAsync(
            tenantId, legalEntityId, includeInactive: false, CancellationToken.None);
        var allRows = await repository.ListByLegalEntityAsync(
            tenantId, legalEntityId, includeInactive: true, CancellationToken.None);

        Assert.Single(activeOnly);
        Assert.Equal("Active", activeOnly[0].Name);
        Assert.Equal(2, allRows.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenIdBelongsToAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var entity = CreateDepartment(otherTenantId, Guid.NewGuid(), "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var result = await repository.GetByIdAsync(tenantId, entity.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEntity_WhenTenantAndIdMatch()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, Guid.NewGuid(), "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var result = await repository.GetByIdAsync(tenantId, entity.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.Id);
    }

    [Fact]
    public async Task GetByIdAsync_DoesNotTrackReturnedRow()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, Guid.NewGuid(), "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        await repository.GetByIdAsync(tenantId, entity.Id, CancellationToken.None);

        Assert.Empty(db.ChangeTracker.Entries<ONEVO.Domain.Features.OrgStructure.Entities.Department>());
    }

    [Fact]
    public async Task GetByIdForLegalEntityAsync_ReturnsNull_WhenIdBelongsToAnotherLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, otherLegalEntityId, "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var result = await repository.GetByIdForLegalEntityAsync(
            tenantId, legalEntityId, entity.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdForLegalEntityAsync_ReturnsNull_WhenIdBelongsToAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(otherTenantId, legalEntityId, "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var result = await repository.GetByIdForLegalEntityAsync(
            tenantId, legalEntityId, entity.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdForLegalEntityAsync_ReturnsEntity_WhenTenantAndLegalEntityAndIdMatch()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, legalEntityId, "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var result = await repository.GetByIdForLegalEntityAsync(
            tenantId, legalEntityId, entity.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.Id);
    }

    [Fact]
    public async Task ExistsByNameAsync_ReturnsTrue_WhenNameAlreadyUsedInSameTenantAndLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.Add(CreateDepartment(tenantId, legalEntityId, "Engineering"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByNameAsync(
            tenantId, legalEntityId, "Engineering", excludingDepartmentId: null, ct: CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsByNameAsync_ReturnsFalse_WhenSameNameOnlyExistsInAnotherLegalEntity()
    {
        // Same department name is allowed in different Companies (legal entities)
        // within the same tenant - the uniqueness scope is per-Company, not tenant-wide.
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        db.Departments.Add(CreateDepartment(tenantId, otherLegalEntityId, "Engineering"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByNameAsync(
            tenantId, legalEntityId, "Engineering", excludingDepartmentId: null, ct: CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsByNameAsync_ReturnsFalse_WhenSameNameOnlyExistsInAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.Add(CreateDepartment(otherTenantId, legalEntityId, "Engineering"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByNameAsync(
            tenantId, legalEntityId, "Engineering", excludingDepartmentId: null, ct: CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsByNameAsync_ExcludesGivenId_ForUpdateSelfCheck()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, legalEntityId, "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByNameAsync(
            tenantId, legalEntityId, "Engineering", excludingDepartmentId: entity.Id, ct: CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenDepartmentBelongsToTenantAndLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, legalEntityId, "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsAsync(tenantId, legalEntityId, entity.Id, CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenDepartmentBelongsToAnotherLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, otherLegalEntityId, "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsAsync(tenantId, legalEntityId, entity.Id, CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsByCodeAsync_ReturnsTrue_WhenCodeMatchesCaseInsensitively()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, legalEntityId, "Operations");
        entity.Code = "OPS";
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByCodeAsync(
            tenantId, legalEntityId, "ops", excludingDepartmentId: null, ct: CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsByCodeAsync_ReturnsFalse_WhenSameCodeOnlyExistsInAnotherLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, otherLegalEntityId, "Operations");
        entity.Code = "OPS";
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByCodeAsync(
            tenantId, legalEntityId, "OPS", excludingDepartmentId: null, ct: CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsByCodeAsync_ExcludesGivenId_ForUpdateSelfCheck()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, legalEntityId, "Operations");
        entity.Code = "OPS";
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByCodeAsync(
            tenantId, legalEntityId, "OPS", excludingDepartmentId: entity.Id, ct: CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsByCodeAsync_ReturnsFalse_WhenNoDepartmentHasThatCode()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.Add(CreateDepartment(tenantId, legalEntityId, "Operations"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var exists = await repository.ExistsByCodeAsync(
            tenantId, legalEntityId, "ZZZ", excludingDepartmentId: null, ct: CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task AddAsync_DoesNotPersistUntilSaveChangesIsCalled()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var repository = new EfDepartmentRepository(db);
        var entity = CreateDepartment(tenantId, Guid.NewGuid(), "Engineering");

        await repository.AddAsync(entity, CancellationToken.None);

        Assert.Equal(0, await db.Departments.CountAsync());
        var entry = db.ChangeTracker.Entries<ONEVO.Domain.Features.OrgStructure.Entities.Department>().Single();
        Assert.Equal(EntityState.Added, entry.State);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingAddAndReturnsAffectedRowCount()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var repository = new EfDepartmentRepository(db);
        var entity = CreateDepartment(tenantId, Guid.NewGuid(), "Engineering");

        await repository.AddAsync(entity, CancellationToken.None);
        var affectedRows = await repository.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, affectedRows);
        Assert.Equal(1, await db.Departments.CountAsync(d => d.TenantId == tenantId));
    }

    [Fact]
    public async Task Update_PersistsChangesToExistingRow()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var entity = CreateDepartment(tenantId, Guid.NewGuid(), "Engineering");
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);
        var fetched = await repository.GetByIdAsync(tenantId, entity.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        fetched.Code = "ENG";

        repository.Update(fetched);
        await repository.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var reloaded = await db.Departments.AsNoTracking().FirstAsync(d => d.Id == entity.Id);
        Assert.Equal("ENG", reloaded.Code);
    }

    [Fact]
    public void DepartmentModel_ConfiguresHeadPositionId_AsNullableForeignKeyToPosition_WithRestrictDeleteBehavior()
    {
        using var db = BuildInMemoryDb();
        var entityType = db.Model.FindEntityType(typeof(ONEVO.Domain.Features.OrgStructure.Entities.Department));
        Assert.NotNull(entityType);

        var property = entityType.FindProperty(nameof(ONEVO.Domain.Features.OrgStructure.Entities.Department.HeadPositionId));
        Assert.NotNull(property);
        Assert.True(property.IsNullable);

        var foreignKey = entityType.GetForeignKeys()
            .FirstOrDefault(fk => fk.Properties.Any(p => p.Name == nameof(ONEVO.Domain.Features.OrgStructure.Entities.Department.HeadPositionId)));
        Assert.NotNull(foreignKey);
        Assert.Equal(typeof(ONEVO.Domain.Features.OrgStructure.Entities.Position), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public async Task CountActiveChildrenAsync_CountsOnlyActiveDirectChildren_ScopedToTenantAndLegalEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var parent = CreateDepartment(tenantId, legalEntityId, "Parent");
        var activeChild = CreateDepartment(tenantId, legalEntityId, "Active Child");
        activeChild.ParentDepartmentId = parent.Id;
        var inactiveChild = CreateDepartment(tenantId, legalEntityId, "Inactive Child");
        inactiveChild.ParentDepartmentId = parent.Id;
        inactiveChild.IsActive = false;
        var otherLegalEntityChild = CreateDepartment(tenantId, Guid.NewGuid(), "Other LE Child");
        otherLegalEntityChild.ParentDepartmentId = parent.Id;

        db.Departments.AddRange(parent, activeChild, inactiveChild, otherLegalEntityChild);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);
        var count = await repository.CountActiveChildrenAsync(
            tenantId, legalEntityId, parent.Id, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CountActiveEmployeesAsync_CountsOnlyActiveStatusEmployees_ScopedToTenantLegalEntityAndDepartment()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        db.EmploymentStatuses.AddRange(
            new ONEVO.Domain.Lookups.EmploymentStatus { Id = 1, Code = "active", Label = "Active" },
            new ONEVO.Domain.Lookups.EmploymentStatus { Id = 4, Code = "terminated", Label = "Terminated" });

        db.Employees.AddRange(
            CreateEmployee(tenantId, legalEntityId, departmentId, employmentStatusId: 1),
            CreateEmployee(tenantId, legalEntityId, departmentId, employmentStatusId: 4),
            CreateEmployee(tenantId, legalEntityId, Guid.NewGuid(), employmentStatusId: 1),
            CreateEmployee(tenantId, Guid.NewGuid(), departmentId, employmentStatusId: 1));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);
        var count = await repository.CountActiveEmployeesAsync(
            tenantId, legalEntityId, departmentId, CancellationToken.None);

        Assert.Equal(1, count);
    }

    #region ListPageByLegalEntityAsync

    [Fact]
    public async Task ListPageByLegalEntityAsync_FiltersBySearch_MatchingNameCaseInsensitively()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.AddRange(
            CreateDepartment(tenantId, legalEntityId, "Engineering"),
            CreateDepartment(tenantId, legalEntityId, "Sales"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var page = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, "engineer", includeInactive: false, parentDepartmentId: null,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 1, pageSize: 25, CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Engineering", page.Items[0].Name);
    }

    [Fact]
    public async Task ListPageByLegalEntityAsync_FiltersBySearch_MatchingCodeCaseInsensitively()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var withCode = CreateDepartment(tenantId, legalEntityId, "Operations");
        withCode.Code = "OPS";
        db.Departments.AddRange(withCode, CreateDepartment(tenantId, legalEntityId, "Sales"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var page = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, "ops", includeInactive: false, parentDepartmentId: null,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 1, pageSize: 25, CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Operations", page.Items[0].Name);
    }

    [Fact]
    public async Task ListPageByLegalEntityAsync_TreatsWhitespaceSearch_AsNoSearch()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.AddRange(
            CreateDepartment(tenantId, legalEntityId, "Engineering"),
            CreateDepartment(tenantId, legalEntityId, "Sales"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var page = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, "   ", includeInactive: false, parentDepartmentId: null,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 1, pageSize: 25, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task ListPageByLegalEntityAsync_ExcludesInactive_UnlessIncludeInactiveTrue()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var inactive = CreateDepartment(tenantId, legalEntityId, "Retired");
        inactive.IsActive = false;
        db.Departments.AddRange(CreateDepartment(tenantId, legalEntityId, "Active"), inactive);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var activeOnly = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, parentDepartmentId: null,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 1, pageSize: 25, CancellationToken.None);
        var allRows = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: true, parentDepartmentId: null,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 1, pageSize: 25, CancellationToken.None);

        Assert.Equal(1, activeOnly.TotalCount);
        Assert.Equal(2, allRows.TotalCount);
    }

    [Fact]
    public async Task ListPageByLegalEntityAsync_ParentDepartmentIdFilter_ReturnsOnlyDirectChildren()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var parent = CreateDepartment(tenantId, legalEntityId, "Parent");
        var child = CreateDepartment(tenantId, legalEntityId, "Child");
        child.ParentDepartmentId = parent.Id;
        var grandchild = CreateDepartment(tenantId, legalEntityId, "Grandchild");
        grandchild.ParentDepartmentId = child.Id;
        db.Departments.AddRange(parent, child, grandchild);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var page = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, parentDepartmentId: parent.Id,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 1, pageSize: 25, CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Child", page.Items[0].Name);
    }

    [Theory]
    [InlineData(DepartmentSortBy.Name, SortDirection.Ascending, "Alpha")]
    [InlineData(DepartmentSortBy.Name, SortDirection.Descending, "Zeta")]
    [InlineData(DepartmentSortBy.Code, SortDirection.Ascending, "Alpha")]
    [InlineData(DepartmentSortBy.Code, SortDirection.Descending, "Zeta")]
    [InlineData(DepartmentSortBy.CreatedAt, SortDirection.Ascending, "Alpha")]
    [InlineData(DepartmentSortBy.CreatedAt, SortDirection.Descending, "Zeta")]
    [InlineData(DepartmentSortBy.UpdatedAt, SortDirection.Ascending, "Alpha")]
    [InlineData(DepartmentSortBy.UpdatedAt, SortDirection.Descending, "Zeta")]
    public async Task ListPageByLegalEntityAsync_Sorts_ByEachFieldAndDirection(
        DepartmentSortBy sortBy, SortDirection sortDirection, string expectedFirstName)
    {
        // Every fixture row has a non-null Code and non-null UpdatedAt on purpose: Postgres
        // orders NULLs last on ASC / first on DESC, InMemory (LINQ-to-Objects) orders NULLs
        // first on ASC - avoiding nulls here keeps this test provider-independent.
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();

        var first = CreateDepartment(tenantId, legalEntityId, "Alpha");
        first.Code = "A-CODE";
        first.CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        first.UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var second = CreateDepartment(tenantId, legalEntityId, "Zeta");
        second.Code = "Z-CODE";
        second.CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        second.UpdatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        db.Departments.AddRange(second, first);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var page = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, parentDepartmentId: null,
            sortBy, sortDirection, page: 1, pageSize: 25, CancellationToken.None);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(expectedFirstName, page.Items[0].Name);
    }

    [Fact]
    public async Task ListPageByLegalEntityAsync_PagesResults_AndReportsTotalCount()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            db.Departments.Add(CreateDepartment(tenantId, legalEntityId, $"Dept {i:00}"));
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var firstPage = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, parentDepartmentId: null,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 1, pageSize: 2, CancellationToken.None);
        var secondPage = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, parentDepartmentId: null,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 2, pageSize: 2, CancellationToken.None);

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal("Dept 00", firstPage.Items[0].Name);
        Assert.Equal("Dept 01", firstPage.Items[1].Name);
        Assert.Equal("Dept 02", secondPage.Items[0].Name);
    }

    [Fact]
    public async Task ListPageByLegalEntityAsync_ReturnsZeroTotalPages_WhenNoMatches()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();

        var repository = new EfDepartmentRepository(db);

        var page = await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, parentDepartmentId: null,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 1, pageSize: 25, CancellationToken.None);

        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task ListPageByLegalEntityAsync_DoesNotTrackReturnedRows()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.Add(CreateDepartment(tenantId, legalEntityId, "Alpha"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        await repository.ListPageByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, parentDepartmentId: null,
            DepartmentSortBy.Name, SortDirection.Ascending, page: 1, pageSize: 25, CancellationToken.None);

        Assert.Empty(db.ChangeTracker.Entries<ONEVO.Domain.Features.OrgStructure.Entities.Department>());
    }

    #endregion

    #region ListForTreeByLegalEntityAsync

    [Fact]
    public async Task ListForTreeByLegalEntityAsync_ReturnsAllMatchingRows_IgnoringPagination()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var parent = CreateDepartment(tenantId, legalEntityId, "Parent");
        var child = CreateDepartment(tenantId, legalEntityId, "Child");
        child.ParentDepartmentId = parent.Id;
        db.Departments.AddRange(parent, child);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var results = await repository.ListForTreeByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ListForTreeByLegalEntityAsync_ExcludesInactive_UnlessIncludeInactiveTrue()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var inactive = CreateDepartment(tenantId, legalEntityId, "Retired");
        inactive.IsActive = false;
        db.Departments.AddRange(CreateDepartment(tenantId, legalEntityId, "Active"), inactive);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var activeOnly = await repository.ListForTreeByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, CancellationToken.None);
        var allRows = await repository.ListForTreeByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: true, CancellationToken.None);

        Assert.Single(activeOnly);
        Assert.Equal(2, allRows.Count);
    }

    [Fact]
    public async Task ListForTreeByLegalEntityAsync_FiltersBySearch_CaseInsensitively()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.AddRange(
            CreateDepartment(tenantId, legalEntityId, "Engineering"),
            CreateDepartment(tenantId, legalEntityId, "Sales"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        var results = await repository.ListForTreeByLegalEntityAsync(
            tenantId, legalEntityId, "ENGINEER", includeInactive: false, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Engineering", results[0].Name);
    }

    [Fact]
    public async Task ListForTreeByLegalEntityAsync_DoesNotTrackReturnedRows()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Departments.Add(CreateDepartment(tenantId, legalEntityId, "Alpha"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfDepartmentRepository(db);

        await repository.ListForTreeByLegalEntityAsync(
            tenantId, legalEntityId, null, includeInactive: false, CancellationToken.None);

        Assert.Empty(db.ChangeTracker.Entries<ONEVO.Domain.Features.OrgStructure.Entities.Department>());
    }

    #endregion

    private static ONEVO.Domain.Features.CoreHr.Entities.Employee CreateEmployee(
        Guid tenantId, Guid legalEntityId, Guid departmentId, int employmentStatusId)
    {
        return new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = Guid.NewGuid(),
            LegalEntityId = legalEntityId,
            DepartmentId = departmentId,
            EmployeeNumber = Guid.NewGuid().ToString("N")[..10],
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{Guid.NewGuid():N}@example.com",
            EmploymentStatusId = employmentStatusId,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
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

    private static ONEVO.Domain.Features.OrgStructure.Entities.Department CreateDepartment(
        Guid tenantId, Guid legalEntityId, string name)
    {
        return new ONEVO.Domain.Features.OrgStructure.Entities.Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = name,
            IsActive = true
        };
    }
}
