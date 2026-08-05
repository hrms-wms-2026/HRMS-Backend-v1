# Department Hardening Part 3 - List/Search/Sort/Pagination/Tree Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn `GET /api/v1/org/legal-entities/{legalEntityId}/departments` into a searchable, sortable, paginated list endpoint that also supports a `view=tree` hierarchy mode, entirely inside `C:\onevoNew\HRMS-Backend-v1`.

**Architecture:** Add two new tenant/legal-entity-scoped repository methods (`ListPageByLegalEntityAsync`, `ListForTreeByLegalEntityAsync`) that push search/filter/sort/paging into the EF query. The existing `ListDepartmentsQuery`/Handler/Validator are rewritten to accept the new query parameters, dispatch to the flat-page or tree repository method based on `view`, and return a `DepartmentListResult` envelope (`Flat` xor `Tree`, never both) so the controller serializes exactly one clean JSON shape. The controller gains the new `[FromQuery]` parameters with their spec'd defaults and unwraps the envelope to `Ok(...)`.

**Tech Stack:** .NET (ONEVO.Api/.Application/.Domain/.Infrastructure), EF Core (Npgsql in prod, InMemory provider in unit tests), MediatR, FluentValidation, xUnit + Moq + FluentAssertions, Testcontainers.PostgreSql for integration tests.

## Global Constraints

- Work only inside `C:\onevoNew\HRMS-Backend-v1`. Do not touch OneVo-HR documentation, frontend, Postman files, Position schema/API/model, Employee schema/API/model, LegalEntity schema/API/model, auth/session/legal/MFA/password code, subscription/module seed code, or logo/file/assets code.
- **Do NOT `git commit` or `git push` anything, at any point, for any reason.** Every task below ends with "move to the next task" instead of a commit step - this deliberately deviates from the writing-plans template, which defaults to committing after each task.
- `head_position_id` stays read-only everywhere: never accept `headPositionId` in any query, route, or request body added in this plan.
- `tenantId` is never accepted from query/body; it always comes from `ICurrentUser.TenantId` inside handlers.
- All new/edited source files must be plain ASCII (no em-dashes, no smart quotes, no non-ASCII punctuation) - this includes the final report markdown file.
- `EfDepartmentRepository.cs` must keep 100% block-bodied members (no `Modifier Method(...) => expr;` on a single line) - an existing architecture test greps for this.
- Search matching must use `.ToLower().Contains(...)`, never `EF.Functions.ILike` - repository unit tests run on `Microsoft.EntityFrameworkCore.InMemory`, which throws on Npgsql-only translations.
- Every new repository method must filter explicitly by `tenantId` and `legalEntityId` in the LINQ `Where`, not rely solely on the EF global tenant query filter (existing convention, existing architecture test enforces it for `IDepartmentRepository`).

---

## Task 1: Repository-level sort/paging types

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/DepartmentSortBy.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/SortDirection.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/DepartmentPage.cs`
- Modify: `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs`

**Interfaces:**
- Produces: `DepartmentSortBy { Name, Code, CreatedAt, UpdatedAt }`, `SortDirection { Ascending, Descending }`, `DepartmentPage(IReadOnlyList<Department> Items, int TotalCount, int Page, int PageSize, int TotalPages)` - all in namespace `ONEVO.Application.Features.OrgStructure.RepositoryInterfaces`. Later tasks depend on these exact names/members.

This task has no independent test of its own (it is pure types + an interface addition); it is verified together with Task 2's repository tests. No commit - move straight to Task 2 once the build below passes.

- [ ] **Step 1: Create the two enums**

`src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/DepartmentSortBy.cs`:
```csharp
namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public enum DepartmentSortBy
{
    Name,
    Code,
    CreatedAt,
    UpdatedAt
}
```

`src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/SortDirection.cs`:
```csharp
namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public enum SortDirection
{
    Ascending,
    Descending
}
```

- [ ] **Step 2: Create the DepartmentPage record**

`src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/DepartmentPage.cs`:
```csharp
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public sealed record DepartmentPage(
    IReadOnlyList<Department> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
```

- [ ] **Step 3: Add the two new methods to IDepartmentRepository**

In `IDepartmentRepository.cs`, insert immediately after `ListByLegalEntityAsync` (keep `ListByLegalEntityAsync` itself untouched - it is still exercised by existing Part 1/2A tests):
```csharp
    Task<DepartmentPage> ListPageByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        string? search,
        bool includeInactive,
        Guid? parentDepartmentId,
        DepartmentSortBy sortBy,
        SortDirection sortDirection,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<IReadOnlyList<Department>> ListForTreeByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        string? search,
        bool includeInactive,
        CancellationToken ct = default);
```

- [ ] **Step 4: Confirm it does not build yet (expected)**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: FAIL - `EfDepartmentRepository` does not implement the two new interface members yet. This is expected; Task 2 fixes it. Do not attempt to make this task build in isolation.

---

## Task 2: EfDepartmentRepository.ListPageByLegalEntityAsync + tests

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs`

**Interfaces:**
- Consumes: `DepartmentSortBy`, `SortDirection`, `DepartmentPage` from Task 1.
- Produces: `EfDepartmentRepository.ListPageByLegalEntityAsync(...)` - later tasks (handler, integration tests) call this by name.

- [ ] **Step 1: Write the failing repository tests**

Add to `EfDepartmentRepositoryTests.cs`, in a new region right before the final closing brace of the class (after the existing `CountActiveEmployeesAsync` test, before the private helpers):

```csharp
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
```

Add `using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;` to the top of `EfDepartmentRepositoryTests.cs` if not already present (it is not - the file currently only imports `ONEVO.Application.Common.ServiceInterfaces`).

- [ ] **Step 2: Run the new tests to verify they fail to compile/fail**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ListPageByLegalEntityAsync" --verbosity minimal`
Expected: FAIL (build error - `EfDepartmentRepository` has no `ListPageByLegalEntityAsync` member yet).

- [ ] **Step 3: Implement ListPageByLegalEntityAsync in EfDepartmentRepository**

Add to `EfDepartmentRepository.cs`, after `ListByLegalEntityAsync` and before `GetByIdAsync`:
```csharp
    public async Task<DepartmentPage> ListPageByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        string? search,
        bool includeInactive,
        Guid? parentDepartmentId,
        DepartmentSortBy sortBy,
        SortDirection sortDirection,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Departments
            .AsNoTracking()
            .Where(department => department.TenantId == tenantId && department.LegalEntityId == legalEntityId);

        if (!includeInactive)
        {
            query = query.Where(department => department.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(department =>
                department.Name.ToLower().Contains(normalizedSearch)
                || (department.Code != null && department.Code.ToLower().Contains(normalizedSearch)));
        }

        if (parentDepartmentId is not null)
        {
            query = query.Where(department => department.ParentDepartmentId == parentDepartmentId.Value);
        }

        query = ApplySort(query, sortBy, sortDirection);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new DepartmentPage(items, totalCount, page, pageSize, totalPages);
    }

    private static IQueryable<Department> ApplySort(
        IQueryable<Department> query, DepartmentSortBy sortBy, SortDirection sortDirection)
    {
        var ascending = sortDirection == SortDirection.Ascending;

        return sortBy switch
        {
            DepartmentSortBy.Code => ascending
                ? query.OrderBy(department => department.Code).ThenBy(department => department.Id)
                : query.OrderByDescending(department => department.Code).ThenBy(department => department.Id),
            DepartmentSortBy.CreatedAt => ascending
                ? query.OrderBy(department => department.CreatedAt).ThenBy(department => department.Id)
                : query.OrderByDescending(department => department.CreatedAt).ThenBy(department => department.Id),
            DepartmentSortBy.UpdatedAt => ascending
                ? query.OrderBy(department => department.UpdatedAt).ThenBy(department => department.Id)
                : query.OrderByDescending(department => department.UpdatedAt).ThenBy(department => department.Id),
            _ => ascending
                ? query.OrderBy(department => department.Name).ThenBy(department => department.Id)
                : query.OrderByDescending(department => department.Name).ThenBy(department => department.Id),
        };
    }
```

- [ ] **Step 4: Run the tests again to verify they pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ListPageByLegalEntityAsync" --verbosity minimal`
Expected: PASS, all 10 new facts/theories green.

No commit - move to Task 3.

---

## Task 3: EfDepartmentRepository.ListForTreeByLegalEntityAsync + tests

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs`

**Interfaces:**
- Produces: `EfDepartmentRepository.ListForTreeByLegalEntityAsync(...)` - Task 5's handler and Task 9's integration tests call this by name.

- [ ] **Step 1: Write the failing tests**

Add to `EfDepartmentRepositoryTests.cs`, right after the `#endregion` closing the `ListPageByLegalEntityAsync` region:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ListForTreeByLegalEntityAsync" --verbosity minimal`
Expected: FAIL (build error - member does not exist).

- [ ] **Step 3: Implement ListForTreeByLegalEntityAsync**

Add to `EfDepartmentRepository.cs`, right after `ListPageByLegalEntityAsync` (before the `ApplySort` private helper):
```csharp
    public async Task<IReadOnlyList<Department>> ListForTreeByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        string? search,
        bool includeInactive,
        CancellationToken ct = default)
    {
        var query = _db.Departments
            .AsNoTracking()
            .Where(department => department.TenantId == tenantId && department.LegalEntityId == legalEntityId);

        if (!includeInactive)
        {
            query = query.Where(department => department.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(department =>
                department.Name.ToLower().Contains(normalizedSearch)
                || (department.Code != null && department.Code.ToLower().Contains(normalizedSearch)));
        }

        query = query.OrderBy(department => department.Name).ThenBy(department => department.Id);

        var results = await query.ToListAsync(ct);
        return results;
    }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ListForTreeByLegalEntityAsync" --verbosity minimal`
Expected: PASS, all 4 new facts green.

No commit - move to Task 4.

---

## Task 4: Response DTOs + DepartmentTreeMapper + mapper tests

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentListPageResponse.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentTreeNodeResponse.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentTreeResponse.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentListResult.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/Mappers/DepartmentTreeMapper.cs`
- Test: Create `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentTreeMapperTests.cs`

**Interfaces:**
- Consumes: `Department` domain entity (existing).
- Produces: `DepartmentListResult(DepartmentListPageResponse? Flat, DepartmentTreeResponse? Tree)`, `DepartmentTreeMapper.BuildTree(IReadOnlyList<Department>) : IReadOnlyList<DepartmentTreeNodeResponse>`. Task 5 (handler) and Task 6 (controller) depend on these exact names.

**Tree behavior decision (documented here, repeated in the final report):** a department whose `ParentDepartmentId` points outside the filtered set passed to `BuildTree` (either because the parent didn't match `search`, is inactive and `includeInactive=false`, or genuinely has no parent) becomes a root node. This keeps the tree well-formed under search/includeInactive filtering without ever dropping a matched department from the response.

- [ ] **Step 1: Write the failing mapper tests**

Create `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentTreeMapperTests.cs`:
```csharp
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Department;

public sealed class DepartmentTreeMapperTests
{
    [Fact]
    public void BuildTree_NestsChildrenUnderParent()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var parent = CreateDepartment(tenantId, legalEntityId, "Parent");
        var child = CreateDepartment(tenantId, legalEntityId, "Child");
        child.ParentDepartmentId = parent.Id;

        var tree = DepartmentTreeMapper.BuildTree(new List<Department> { parent, child });

        Assert.Single(tree);
        Assert.Equal("Parent", tree[0].Name);
        Assert.Single(tree[0].Children);
        Assert.Equal("Child", tree[0].Children[0].Name);
    }

    [Fact]
    public void BuildTree_TreatsDepartmentWithParentOutsideSet_AsRoot()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var orphan = CreateDepartment(tenantId, legalEntityId, "Orphan");
        orphan.ParentDepartmentId = Guid.NewGuid();

        var tree = DepartmentTreeMapper.BuildTree(new List<Department> { orphan });

        Assert.Single(tree);
        Assert.Equal("Orphan", tree[0].Name);
        Assert.Empty(tree[0].Children);
    }

    [Fact]
    public void BuildTree_DoesNotExposeTenantId()
    {
        var properties = typeof(DepartmentTreeNodeResponse).GetProperties();

        Assert.DoesNotContain(properties, p => p.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildTree_PreservesHeadPositionId_ReadOnly()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var headPositionId = Guid.NewGuid();
        var department = CreateDepartment(tenantId, legalEntityId, "Has Head");
        department.HeadPositionId = headPositionId;

        var tree = DepartmentTreeMapper.BuildTree(new List<Department> { department });

        Assert.Equal(headPositionId, tree[0].HeadPositionId);
    }

    private static Department CreateDepartment(Guid tenantId, Guid legalEntityId, string name)
    {
        return new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = name,
            IsActive = true
        };
    }
}
```

- [ ] **Step 2: Run to verify failure (types do not exist yet)**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~DepartmentTreeMapperTests" --verbosity minimal`
Expected: FAIL (build error).

- [ ] **Step 3: Create the response DTOs**

`DepartmentListPageResponse.cs`:
```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentListPageResponse(
    IReadOnlyList<DepartmentListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
```

`DepartmentTreeNodeResponse.cs`:
```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentTreeNodeResponse(
    Guid Id,
    Guid LegalEntityId,
    string Name,
    string? Code,
    Guid? ParentDepartmentId,
    Guid? HeadPositionId,
    bool IsActive,
    IReadOnlyList<DepartmentTreeNodeResponse> Children);
```

`DepartmentTreeResponse.cs`:
```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentTreeResponse(
    IReadOnlyList<DepartmentTreeNodeResponse> TreeItems);
```

`DepartmentListResult.cs`:
```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public sealed record DepartmentListResult(
    DepartmentListPageResponse? Flat,
    DepartmentTreeResponse? Tree);
```

- [ ] **Step 4: Create DepartmentTreeMapper**

`src/ONEVO.Application/Features/OrgStructure/Department/Mappers/DepartmentTreeMapper.cs`:
```csharp
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class DepartmentTreeMapper
{
    public static IReadOnlyList<DepartmentTreeNodeResponse> BuildTree(IReadOnlyList<Department> departments)
    {
        var idsInSet = departments.Select(department => department.Id).ToHashSet();

        var childrenByParentId = departments
            .Where(department => department.ParentDepartmentId is not null
                && idsInSet.Contains(department.ParentDepartmentId.Value))
            .GroupBy(department => department.ParentDepartmentId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(department => department.Name).ToList());

        var roots = departments
            .Where(department => department.ParentDepartmentId is null
                || !idsInSet.Contains(department.ParentDepartmentId.Value))
            .OrderBy(department => department.Name)
            .ToList();

        return roots.Select(root => BuildNode(root, childrenByParentId)).ToList();
    }

    private static DepartmentTreeNodeResponse BuildNode(
        Department department, IReadOnlyDictionary<Guid, List<Department>> childrenByParentId)
    {
        var children = childrenByParentId.TryGetValue(department.Id, out var childDepartments)
            ? childDepartments.Select(child => BuildNode(child, childrenByParentId)).ToList()
            : new List<DepartmentTreeNodeResponse>();

        return new DepartmentTreeNodeResponse(
            department.Id,
            department.LegalEntityId,
            department.Name,
            department.Code,
            department.ParentDepartmentId,
            department.HeadPositionId,
            department.IsActive,
            children);
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~DepartmentTreeMapperTests" --verbosity minimal`
Expected: PASS, all 4 facts green.

No commit - move to Task 5.

---

## Task 5: ListDepartmentsQuery + Validator rewrite + validator tests

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQuery.cs`
- Modify: `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQueryValidator.cs`
- Test: Modify `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs`

**Interfaces:**
- Produces: `ListDepartmentsQuery(Guid LegalEntityId, string? Search, bool IncludeInactive, Guid? ParentDepartmentId, string View, string SortBy, string SortDirection, int Page, int PageSize) : IRequest<Result<DepartmentListResult>>`. Task 6 (handler) and Task 7 (controller) depend on this exact constructor order.
- `View`/`SortBy`/`SortDirection` stay as raw strings on the query (matching the literal `flat|tree`, `name|code|createdAt|updatedAt`, `asc|desc` wire values) - the handler parses them to the `DepartmentSortBy`/`SortDirection` enums from Task 1 only after FluentValidation has already guaranteed they are one of the allowed values.

- [ ] **Step 1: Rewrite ListDepartmentsQuery.cs**

Replace the full file content:
```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;

public record ListDepartmentsQuery(
    Guid LegalEntityId,
    string? Search,
    bool IncludeInactive,
    Guid? ParentDepartmentId,
    string View,
    string SortBy,
    string SortDirection,
    int Page,
    int PageSize) : IRequest<Result<DepartmentListResult>>;
```

- [ ] **Step 2: Rewrite ListDepartmentsQueryValidator.cs**

Replace the full file content:
```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;

public class ListDepartmentsQueryValidator : AbstractValidator<ListDepartmentsQuery>
{
    private static readonly string[] AllowedViews = ["flat", "tree"];
    private static readonly string[] AllowedSortBy = ["name", "code", "createdat", "updatedat"];
    private static readonly string[] AllowedSortDirections = ["asc", "desc"];

    public ListDepartmentsQueryValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.Search)
            .MaximumLength(100).WithMessage("Search cannot exceed 100 characters.");

        RuleFor(x => x.View)
            .NotEmpty().WithMessage("View is required.")
            .Must(view => AllowedViews.Contains(view.Trim().ToLowerInvariant()))
            .WithMessage("View must be 'flat' or 'tree'.")
            .When(x => !string.IsNullOrEmpty(x.View));

        RuleFor(x => x.SortBy)
            .NotEmpty().WithMessage("SortBy is required.")
            .Must(sortBy => AllowedSortBy.Contains(sortBy.Trim().ToLowerInvariant()))
            .WithMessage("SortBy must be one of: name, code, createdAt, updatedAt.")
            .When(x => !string.IsNullOrEmpty(x.SortBy));

        RuleFor(x => x.SortDirection)
            .NotEmpty().WithMessage("SortDirection is required.")
            .Must(direction => AllowedSortDirections.Contains(direction.Trim().ToLowerInvariant()))
            .WithMessage("SortDirection must be 'asc' or 'desc'.")
            .When(x => !string.IsNullOrEmpty(x.SortDirection));

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");
    }
}
```

- [ ] **Step 3: Write validator unit tests**

`ListDepartmentsQuery` is constructed directly (no handler needed) in `DepartmentApplicationUnitTests.cs`. Add a new region at the end of the class, immediately before the closing brace of `DepartmentApplicationUnitTests`, after the private `CreateDepartment` helper's closing brace - actually insert it as a new `#region` right after the existing `#region Tenant Context Isolation Guard ... #endregion` block:

```csharp
    #region ListDepartmentsQueryValidator

    [Fact]
    public void ListDepartmentsQueryValidator_AcceptsDefaultValues()
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ListDepartmentsQueryValidator_RejectsPageLessThanOne()
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", "asc", 0, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ListDepartmentsQueryValidator_RejectsPageSizeOutOfRange(int pageSize)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", "asc", 1, pageSize);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.PageSize));
    }

    [Fact]
    public void ListDepartmentsQueryValidator_AcceptsPageSizeAtUpperBound()
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", "asc", 1, 100);

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public void ListDepartmentsQueryValidator_RejectsInvalidSortBy(string sortBy)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", sortBy, "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.SortBy));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public void ListDepartmentsQueryValidator_RejectsInvalidSortDirection(string sortDirection)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", sortDirection, 1, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.SortDirection));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public void ListDepartmentsQueryValidator_RejectsInvalidView(string view)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, view, "name", "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.View));
    }

    [Fact]
    public void ListDepartmentsQueryValidator_RejectsSearchLongerThan100Characters()
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, new string('a', 101), false, null, "flat", "name", "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.Search));
    }

    [Theory]
    [InlineData("TREE")]
    [InlineData("tree")]
    [InlineData("Flat")]
    public void ListDepartmentsQueryValidator_AcceptsViewCaseInsensitively(string view)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, view, "name", "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }

    #endregion
```

Note: this same edit pass must also fix the two now-broken `ListDepartments*` tests already in this file (`ListDepartments_ReturnsOnlyDepartmentsInSelectedLegalEntity`, `ListDepartments_ReturnsNotFound_WhenLegalEntityDoesNotExist`) plus `Handlers_DoNotAcceptTenantIdFromRequestInput_ResolvesFromCurrentUserOnly` - these all construct `new ListDepartmentsQuery(_legalEntityId)` with the old 2-arg constructor and mock the now-removed `ListByLegalEntityAsync` call pattern for this handler. Task 6, Step 1 below replaces the entire `#region ListDepartments ... #endregion` block (which contains those three tests) with the new handler tests, so do not fix them here - leave the compile error in place; it will be resolved in Task 6.

- [ ] **Step 4: Run only the new validator tests to verify they pass despite the rest of the file not compiling yet**

This project will not build cleanly until Task 6 fixes the remaining `ListDepartments` region, so skip running tests here. Proceed directly to Task 6 - Steps 1-2 of Task 6 fix the compile error and Step 4 of Task 6 runs the whole file (including this task's new validator tests).

No commit - move to Task 6.

---

## Task 6: ListDepartmentsQueryHandler rewrite + handler tests

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQueryHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs`

**Interfaces:**
- Consumes: `ListDepartmentsQuery` (Task 5), `IDepartmentRepository.ListPageByLegalEntityAsync`/`ListForTreeByLegalEntityAsync` (Tasks 2-3), `DepartmentTreeMapper.BuildTree` (Task 4).
- Produces: `Result<DepartmentListResult>` - Task 7 (controller) depends on this exact return shape.

- [ ] **Step 1: Replace the `#region ListDepartments ... #endregion` block in DepartmentApplicationUnitTests.cs**

The existing block (currently containing `ListDepartments_ReturnsOnlyDepartmentsInSelectedLegalEntity` and `ListDepartments_ReturnsNotFound_WhenLegalEntityDoesNotExist`) must be replaced in full with:

```csharp
    #region ListDepartments

    private static ListDepartmentsQuery DefaultListQuery(
        Guid legalEntityId,
        string? search = null,
        bool includeInactive = false,
        Guid? parentDepartmentId = null,
        string view = "flat",
        string sortBy = "name",
        string sortDirection = "asc",
        int page = 1,
        int pageSize = 25)
    {
        return new ListDepartmentsQuery(
            legalEntityId, search, includeInactive, parentDepartmentId, view, sortBy, sortDirection, page, pageSize);
    }

    [Fact]
    public async Task ListDepartments_FlatView_ReturnsFlatPage_AndNullTree()
    {
        var dept1 = CreateDepartment(_tenantId, _legalEntityId, "Engineering");
        var page = new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department> { dept1 }, 1, 1, 25, 1);

        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Flat);
        Assert.Null(result.Value.Tree);
        Assert.Single(result.Value.Flat!.Items);
        Assert.Equal(1, result.Value.Flat.TotalCount);
    }

    [Fact]
    public async Task ListDepartments_ReturnsNotFound_WhenLegalEntityDoesNotExist()
    {
        var invalidLegalEntityId = Guid.NewGuid();
        _legalEntityRepoMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, invalidLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.LegalEntity?)null);

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(invalidLegalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ListDepartments_TrimsSearch_BeforePassingToRepository()
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, "engineering", false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, search: "  engineering  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, "engineering", false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListDepartments_TreatsEmptyOrWhitespaceSearch_AsNoSearch(string search)
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, search: search), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListDepartments_ForwardsParentDepartmentIdToRepository()
    {
        var parentId = Guid.NewGuid();
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, parentId, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, parentDepartmentId: parentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, parentId, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListDepartments_IncludeInactiveTrue_ForwardsToRepository()
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, true, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, includeInactive: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, true, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("code", DepartmentSortBy.Code)]
    [InlineData("CREATEDAT", DepartmentSortBy.CreatedAt)]
    [InlineData("updatedAt", DepartmentSortBy.UpdatedAt)]
    [InlineData("name", DepartmentSortBy.Name)]
    public async Task ListDepartments_ParsesSortBy_CaseInsensitively(string sortByInput, DepartmentSortBy expected)
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, null, expected, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, sortBy: sortByInput), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, null, expected, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("DESC", SortDirection.Descending)]
    [InlineData("asc", SortDirection.Ascending)]
    public async Task ListDepartments_ParsesSortDirection_CaseInsensitively(string input, SortDirection expected)
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, expected, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, sortDirection: input), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, expected, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListDepartments_TreeView_CallsTreeRepositoryMethod_NotPageMethod()
    {
        var parent = CreateDepartment(_tenantId, _legalEntityId, "Parent");
        var child = CreateDepartment(_tenantId, _legalEntityId, "Child");
        child.ParentDepartmentId = parent.Id;

        _departmentRepoMock
            .Setup(d => d.ListForTreeByLegalEntityAsync(_tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Features.OrgStructure.Entities.Department> { parent, child });

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, view: "tree"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Flat);
        Assert.NotNull(result.Value.Tree);
        Assert.Single(result.Value.Tree!.TreeItems);
        Assert.Single(result.Value.Tree.TreeItems[0].Children);
        _departmentRepoMock.Verify(d => d.ListForTreeByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()), Times.Once);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Guid?>(),
            It.IsAny<DepartmentSortBy>(), It.IsAny<SortDirection>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListDepartments_TreeView_IgnoresParentDepartmentIdAndPagination()
    {
        _departmentRepoMock
            .Setup(d => d.ListForTreeByLegalEntityAsync(_tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Features.OrgStructure.Entities.Department>
            {
                CreateDepartment(_tenantId, _legalEntityId, "Root")
            });

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            DefaultListQuery(_legalEntityId, view: "tree", parentDepartmentId: Guid.NewGuid(), page: 2, pageSize: 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Tree!.TreeItems);
        _departmentRepoMock.Verify(d => d.ListForTreeByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListDepartments_TreeView_DoesNotExposeTenantId()
    {
        _departmentRepoMock
            .Setup(d => d.ListForTreeByLegalEntityAsync(_tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Features.OrgStructure.Entities.Department>
            {
                CreateDepartment(_tenantId, _legalEntityId, "Root")
            });

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        await handler.Handle(DefaultListQuery(_legalEntityId, view: "tree"), CancellationToken.None);

        var properties = typeof(DepartmentTreeNodeResponse).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
```

Also fix `Handlers_DoNotAcceptTenantIdFromRequestInput_ResolvesFromCurrentUserOnly` (in the `#region Tenant Context Isolation Guard` block near the bottom of the file) - replace its body's `new ListDepartmentsQuery(_legalEntityId)` call with `DefaultListQuery(_legalEntityId)`.

Add `using ONEVO.Application.Features.OrgStructure.DTOs.Responses;` to the top of `DepartmentApplicationUnitTests.cs` (needed for `DepartmentTreeNodeResponse` in the new tests; `DepartmentPage`/`DepartmentSortBy`/`SortDirection` are already reachable via the existing `using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;`).

- [ ] **Step 2: Run to verify failure (handler not yet rewritten)**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ListDepartments" --verbosity minimal`
Expected: FAIL (build error - `ListDepartmentsQueryHandler` constructor/`Handle` signature still returns the old `Result<IReadOnlyList<DepartmentListItemResponse>>>` and still calls `ListByLegalEntityAsync`).

- [ ] **Step 3: Rewrite ListDepartmentsQueryHandler.cs**

Replace the full file content:
```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;

public class ListDepartmentsQueryHandler
    : IRequestHandler<ListDepartmentsQuery, Result<DepartmentListResult>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public ListDepartmentsQueryHandler(
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<DepartmentListResult>> Handle(
        ListDepartmentsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<DepartmentListResult>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<DepartmentListResult>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<DepartmentListResult>.NotFound("Legal entity not found.");

        var normalizedSearch = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var normalizedView = request.View.Trim().ToLowerInvariant();

        if (normalizedView == "tree")
        {
            var treeDepartments = await _departments.ListForTreeByLegalEntityAsync(
                tenantId, request.LegalEntityId, normalizedSearch, request.IncludeInactive, ct);
            var treeItems = DepartmentTreeMapper.BuildTree(treeDepartments);

            return Result<DepartmentListResult>.Success(
                new DepartmentListResult(Flat: null, Tree: new DepartmentTreeResponse(treeItems)));
        }

        var sortBy = ParseSortBy(request.SortBy);
        var sortDirection = ParseSortDirection(request.SortDirection);

        var page = await _departments.ListPageByLegalEntityAsync(
            tenantId,
            request.LegalEntityId,
            normalizedSearch,
            request.IncludeInactive,
            request.ParentDepartmentId,
            sortBy,
            sortDirection,
            request.Page,
            request.PageSize,
            ct);

        var items = page.Items.Select(DepartmentMapper.ToListItemResponse).ToList();
        var flat = new DepartmentListPageResponse(items, page.Page, page.PageSize, page.TotalCount, page.TotalPages);

        return Result<DepartmentListResult>.Success(new DepartmentListResult(Flat: flat, Tree: null));
    }

    private static DepartmentSortBy ParseSortBy(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "name" => DepartmentSortBy.Name,
            "code" => DepartmentSortBy.Code,
            "createdat" => DepartmentSortBy.CreatedAt,
            "updatedat" => DepartmentSortBy.UpdatedAt,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sortBy value.")
        };
    }

    private static SortDirection ParseSortDirection(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "asc" => SortDirection.Ascending,
            "desc" => SortDirection.Descending,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sortDirection value.")
        };
    }
}
```

The `ParseSortBy`/`ParseSortDirection` default-throw arms are unreachable in production because `ListDepartmentsQueryValidator` (Task 5) already rejects any other value before the handler runs in the MediatR pipeline - they exist only so the switch expression is exhaustive, not as defensive/duplicate validation.

- [ ] **Step 4: Run the whole unit test project to verify Tasks 1-6 are all green together**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: PASS - full project builds and all tests (old + new) are green. If `DepartmentsControllerTests.cs` now fails to build (it will, because `DepartmentsController.List` still has the old 3-arg signature while these test edits already reference the new one only from Task 7 onward) - it does NOT yet, since Task 6 does not touch `DepartmentsControllerTests.cs`. The existing `List_*` tests in `DepartmentsControllerTests.cs` still compile fine against the old controller signature at this point (they only break once Task 7 changes the controller). If for any reason the build fails referencing `DepartmentsControllerTests.cs` here, stop and re-check you have not prematurely edited the controller.

No commit - move to Task 7.

---

## Task 7: DepartmentsController.List rewrite + controller tests

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs`

**Interfaces:**
- Consumes: `ListDepartmentsQuery` (Task 5), `Result<DepartmentListResult>` (Task 6).
- Produces: `DepartmentsController.List(...)` with 9 `[FromQuery]` parameters plus route `legalEntityId` and `CancellationToken ct` - Task 9's integration tests hit this via real HTTP, not by name.

- [ ] **Step 1: Write the failing controller tests**

In `DepartmentsControllerTests.cs`, replace the three existing `List_*` test methods (`List_SendsQuery_WithRouteLegalEntityId_AndIncludeInactiveFalseByDefault`, `List_SendsQuery_WithIncludeInactiveTrue_WhenParameterIsTrue`, `List_ForbiddenResult_ReturnsProblem403`) with:

```csharp
    [Fact]
    public async Task List_UsesDefaultQueryValues_WhenNoneProvided()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentListResult>.Success(
                new DepartmentListResult(new DepartmentListPageResponse([], 1, 25, 0, 0), null)));

        var result = await _sut.List(_legalEntityId, ct: CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ListDepartmentsQuery>(q =>
                q.LegalEntityId == _legalEntityId &&
                q.Search == null &&
                q.IncludeInactive == false &&
                q.ParentDepartmentId == null &&
                q.View == "flat" &&
                q.SortBy == "name" &&
                q.SortDirection == "asc" &&
                q.Page == 1 &&
                q.PageSize == 25),
            It.IsAny<CancellationToken>()), Times.Once);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DepartmentListPageResponse>();
    }

    [Fact]
    public async Task List_ForwardsExplicitQueryValues_ToMediator()
    {
        var parentId = Guid.NewGuid();
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentListResult>.Success(
                new DepartmentListResult(new DepartmentListPageResponse([], 2, 10, 0, 0), null)));

        var result = await _sut.List(
            _legalEntityId,
            search: "eng",
            includeInactive: true,
            parentDepartmentId: parentId,
            view: "flat",
            sortBy: "code",
            sortDirection: "desc",
            page: 2,
            pageSize: 10,
            ct: CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ListDepartmentsQuery>(q =>
                q.LegalEntityId == _legalEntityId &&
                q.Search == "eng" &&
                q.IncludeInactive == true &&
                q.ParentDepartmentId == parentId &&
                q.View == "flat" &&
                q.SortBy == "code" &&
                q.SortDirection == "desc" &&
                q.Page == 2 &&
                q.PageSize == 10),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task List_TreeView_ReturnsTreePayload()
    {
        var treeResponse = new DepartmentTreeResponse(
        [
            new DepartmentTreeNodeResponse(_departmentId, _legalEntityId, "Engineering", "ENG", null, null, true, [])
        ]);
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentListResult>.Success(new DepartmentListResult(null, treeResponse)));

        var result = await _sut.List(_legalEntityId, view: "tree", ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DepartmentTreeResponse>();
        ok.Value.Should().Be(treeResponse);
    }

    [Fact]
    public async Task List_ForbiddenResult_ReturnsProblem403()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ListDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentListResult>.Forbidden("Forbidden context."));

        var result = await _sut.List(_legalEntityId, ct: CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(403);
    }

    [Fact]
    public void List_HasNoTenantIdOrHeadPositionIdParameter()
    {
        var listMethod = typeof(DepartmentsController).GetMethod(nameof(DepartmentsController.List));
        var parameterNames = listMethod!.GetParameters().Select(p => p.Name).ToList();

        parameterNames.Should().NotContain(name => string.Equals(name, "tenantId", StringComparison.OrdinalIgnoreCase));
        parameterNames.Should().NotContain(name => string.Equals(name, "headPositionId", StringComparison.OrdinalIgnoreCase));
    }
```

`DepartmentsControllerTests.cs` already has `using ONEVO.Application.Features.OrgStructure.DTOs.Responses;` and `using System.Reflection;` is not currently imported - add `using System.Reflection;` and `using System.Linq;` to the top if not already implicitly available (this project has ImplicitUsings enabled like the others, so `System.Linq`/basic reflection extension methods resolve without explicit usings; add `using System.Reflection;` only if `GetMethod`/`GetParameters` do not resolve without it - they are instance/static members on `Type`/`MethodInfo` from `System` and `System.Reflection`, so add the explicit `using System.Reflection;` to be safe).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~DepartmentsControllerTests" --verbosity minimal`
Expected: FAIL (build error - `DepartmentsController.List` still has the old signature).

- [ ] **Step 3: Rewrite the List action in DepartmentsController.cs**

Replace:
```csharp
    /// <summary>List departments for a specific legal entity. Active departments only unless includeInactive=true.</summary>
    [HttpGet]
    [RequirePermission("org:read")]
    public async Task<IActionResult> List(
        Guid legalEntityId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListDepartmentsQuery(legalEntityId, includeInactive), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```
with:
```csharp
    /// <summary>List departments for a specific legal entity. Supports search, sort, pagination,
    /// and an optional hierarchy (tree) view. Active departments only unless includeInactive=true.
    /// view=tree returns the full legal-entity hierarchy with search/includeInactive applied;
    /// parentDepartmentId, page, and pageSize are ignored in tree mode - see
    /// DEPARTMENT_HARDENING_PART3_LIST_SEARCH_PAGINATION_REPORT.md for the documented decision.</summary>
    [HttpGet]
    [RequirePermission("org:read")]
    public async Task<IActionResult> List(
        Guid legalEntityId,
        [FromQuery] string? search = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] Guid? parentDepartmentId = null,
        [FromQuery] string view = "flat",
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortDirection = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListDepartmentsQuery(
                legalEntityId, search, includeInactive, parentDepartmentId, view, sortBy, sortDirection, page, pageSize),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        object payload = result.Value!.Tree is not null ? result.Value.Tree : result.Value.Flat!;
        return Ok(payload);
    }
```

- [ ] **Step 4: Run the full unit test project**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: PASS - every test in the project (Department and non-Department) is green.

No commit - move to Task 8.

---

## Task 8: Architecture tests

**Files:**
- Modify: `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs`
- Create: `tests/ONEVO.Tests.Architecture/DepartmentPart3ArchitectureTests.cs`

**Interfaces:**
- Consumes: `IDepartmentRepository` (Task 1), `ListDepartmentsQuery` (Task 5).

Six of the eight architecture bullets from the task spec are already green and untouched by this plan (request contracts have no TenantId/HeadPositionId, controller injects IMediator only, controller has no DbContext/repository fields, base route unchanged, org:read on list/get, org:manage on mutations) - `DepartmentsControllerArchitectureTests.cs` and `DepartmentPart2BArchitectureTests.cs` already assert all of these and none of Tasks 1-7 changed the things they check (route template, permission attributes, constructor shape). Only two are genuinely new for Part 3: "no new Department migrations" and "no Position schema/API/entity changes" - plus extending the existing legal-entity-scoped-method check to cover the two new repository methods.

- [ ] **Step 1: Extend the existing legal-entity-scoped-methods theory in DepartmentPart2AArchitectureTests.cs**

Add two more `[InlineData(...)]` lines to the `IDepartmentRepository_LegalEntityScopedMethods_HaveALegalEntityIdParameter` theory (do not touch `ListByLegalEntityAsync` or any of the other existing InlineData lines):
```csharp
    [InlineData(nameof(IDepartmentRepository.ListByLegalEntityAsync))]
    [InlineData(nameof(IDepartmentRepository.GetByIdForLegalEntityAsync))]
    [InlineData(nameof(IDepartmentRepository.ExistsByNameAsync))]
    [InlineData(nameof(IDepartmentRepository.ExistsAsync))]
    [InlineData(nameof(IDepartmentRepository.ExistsByCodeAsync))]
    [InlineData(nameof(IDepartmentRepository.IsDescendantAsync))]
    [InlineData(nameof(IDepartmentRepository.CountActiveChildrenAsync))]
    [InlineData(nameof(IDepartmentRepository.CountActiveEmployeesAsync))]
    [InlineData(nameof(IDepartmentRepository.ListPageByLegalEntityAsync))]
    [InlineData(nameof(IDepartmentRepository.ListForTreeByLegalEntityAsync))]
    public void IDepartmentRepository_LegalEntityScopedMethods_HaveALegalEntityIdParameter(string methodName)
```
(only the two new `InlineData` lines are additions; the method body and everything else in the theory stays as-is.) `IDepartmentRepository_EveryReadMethodHasATenantIdParameter` in the same file already iterates every interface method generically (excluding `AddAsync`/`Update`/`SaveChangesAsync`), so it automatically covers the two new methods for the tenantId check with no edit needed.

- [ ] **Step 2: Create DepartmentPart3ArchitectureTests.cs**

```csharp
using System.Reflection;
using ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards Part 3 scope for Department: list/search/sort/pagination/tree read model.
/// The controller-shape, permission, and request-contract checks for Department already
/// live in DepartmentsControllerArchitectureTests.cs / DepartmentPart2BArchitectureTests.cs
/// and are unaffected by Part 3 - this file only adds what is genuinely new: the list
/// query still excludes TenantId/HeadPositionId, no Department migration was added beyond
/// the three that already existed before Part 3, and Position was not touched.
/// </summary>
public sealed class DepartmentPart3ArchitectureTests
{
    [Fact]
    public void ListDepartmentsQuery_DoesNotContainTenantIdOrHeadPositionId()
    {
        var properties = typeof(ListDepartmentsQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(properties, name => string.Equals(name, "TenantId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => string.Equals(name, "HeadPositionId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoNewDepartmentMigrations_WereAddedInPart3()
    {
        var migrationsDir = FindDirectoryUnderRepoRoot("src", "ONEVO.Infrastructure", "Migrations");

        var departmentMigrationFiles = Directory.GetFiles(migrationsDir, "*Department*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name)
            .ToList();

        var expected = new[]
        {
            "20260803085109_AddDepartments",
            "20260803092715_AddDepartmentHeadPositionId",
            "20260804053523_AddDepartmentCodeCaseInsensitiveUniqueIndex"
        }.OrderBy(name => name).ToList();

        Assert.Equal(expected, departmentMigrationFiles);
    }

    [Fact]
    public void PositionEntity_HasNoDepartmentIdProperty()
    {
        var property = typeof(Position).GetProperty("DepartmentId");

        Assert.Null(property);
    }

    private static string FindDirectoryUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
```

- [ ] **Step 3: Run the architecture test project**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Expected: PASS - all Department architecture tests (Part1/Part2A/Part2B/Part2ArchiveRestore/Controller/Part3) green.

No commit - move to Task 9.

---

## Task 9: Integration tests (fix breakage + add new coverage)

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs`

**Interfaces:**
- Consumes: the live HTTP endpoint from Task 7.

**Critical fix first:** the flat list response shape changed from a bare JSON array to `{items:[...], page, pageSize, totalCount, totalPages}`. Two existing tests call `.EnumerateArray()` directly on the list response and will throw `InvalidOperationException` (wrong JsonValueKind) once Task 7 ships, even though nothing about search/sort/pagination itself is broken - this must be fixed by inspection regardless of whether Docker/Testcontainers is available in this environment, since it is a compile-time-invisible, run-time-only break.

- [ ] **Step 1: Fix `Create_Get_Update_Delete_FullLifecycle`**

Replace:
```csharp
        var defaultList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments");
        defaultList.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?includeInactive=true");
        inclusiveList.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task Create_DuplicateNameInSameLegalEntity_Returns409()
```
with:
```csharp
        var defaultList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments");
        defaultList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?includeInactive=true");
        inclusiveList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task Create_DuplicateNameInSameLegalEntity_Returns409()
```
(this anchors the edit uniquely via the following method's signature so it does not collide with the second occurrence fixed in Step 2.)

- [ ] **Step 2: Fix `Archive_Route_SoftDeactivates_AndListExcludesByDefault`**

Replace:
```csharp
        var defaultList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments");
        defaultList.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?includeInactive=true");
        inclusiveList.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    // -- Cross-tenant / cross-legal-entity isolation -------------------------
```
with:
```csharp
        var defaultList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments");
        defaultList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?includeInactive=true");
        inclusiveList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    // -- Cross-tenant / cross-legal-entity isolation -------------------------
```

Do NOT touch `GetPrimaryLegalEntityIdAsync`'s `list.EnumerateArray().Single(...)` call - that hits `/api/v1/org/legal-entities`, a different (unchanged) endpoint.

- [ ] **Step 3: Add new Part 3 integration tests**

Insert a new block right before the `// -- Fixture provisioning helpers` comment:
```csharp
    // -- Part 3: search, sort, pagination, tree ------------------------------

    [Fact]
    public async Task List_ReturnsOnlyDepartmentsForSelectedLegalEntity()
    {
        var deptInFirstLe = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "List Isolation LE1");
        var deptInSecondLe = await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, "List Isolation LE2");

        var firstLeList = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments");
        var ids = firstLeList.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(deptInFirstLe.GetProperty("id").GetGuid());
        ids.Should().NotContain(deptInSecondLe.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_Search_ReturnsOnlyMatchingDepartments_ScopedToLegalEntity()
    {
        await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Search Match Marketing");
        await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Search NoMatch Finance");

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?search=marketing");

        var names = response.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();
        names.Should().Contain("Search Match Marketing");
        names.Should().NotContain("Search NoMatch Finance");
    }

    [Fact]
    public async Task List_Pagination_ReturnsCorrectTotalCountAndPageItems()
    {
        for (var i = 0; i < 3; i++)
        {
            await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, $"Page Dept {i}");
        }

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments?page=1&pageSize=2");

        response.GetProperty("totalCount").GetInt32().Should().Be(3);
        response.GetProperty("page").GetInt32().Should().Be(1);
        response.GetProperty("pageSize").GetInt32().Should().Be(2);
        response.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task List_TreeView_ReturnsHierarchyForSelectedLegalEntityOnly()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Tree Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Tree Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, "Other LE Root");

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?view=tree");

        response.TryGetProperty("treeItems", out var treeItems).Should().BeTrue();
        var parentNode = treeItems.EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == parentId);
        parentNode.GetProperty("children").GetArrayLength().Should().Be(1);
        treeItems.EnumerateArray().Select(n => n.GetProperty("name").GetString()).Should().NotContain("Other LE Root");
    }

    [Fact]
    public async Task List_TreeView_DoesNotExposeTenantId()
    {
        await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Tree No Tenant");

        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?view=tree",
            body: null, cookie: _tenantAOwner.SessionCookie);
        var text = await response.Content.ReadAsStringAsync();

        text.Should().NotContain("tenantId", "tree responses must not expose the tenant id");
    }

    [Fact]
    public async Task List_InvalidSortBy_Returns400()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?sortBy=nope",
            body: null, cookie: _tenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_PageSizeOverMax_Returns400()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments?pageSize=101",
            body: null, cookie: _tenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_ParentDepartmentIdFilter_ReturnsOnlyDirectChildren()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantASecondLegalEntityId, "Filter Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        var childResponse = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments",
            new { name = "Filter Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var childId = (await ReadJsonAsync(childResponse)).GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments",
            new { name = "Filter Grandchild", parentDepartmentId = childId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await GetJsonAsync(_tenantAOwner,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/departments?parentDepartmentId={parentId}");

        var names = response.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();
        names.Should().ContainSingle().Which.Should().Be("Filter Child");
    }

```

- [ ] **Step 4: Run the Department integration tests if Docker is available**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` (sanity build first)
Then run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "FullyQualifiedName~Department" --verbosity minimal`

If Docker is not available in this environment, this command will fail at container startup (Testcontainers cannot start `postgres:16-alpine`) rather than at test-assertion time - if that happens, record in the final report that integration tests did not actually execute and why, rather than claiming they passed. Do not silently skip reporting this.

No commit - move to Task 10.

---

## Task 10: Full verification pass + report

**Files:**
- Create: `DEPARTMENT_HARDENING_PART3_LIST_SEARCH_PAGINATION_REPORT.md` (repo root, alongside the existing `DEPARTMENT_*.md` files)

- [ ] **Step 1: Run the exact verification commands from the task spec, in order**

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "FullyQualifiedName~Department" --verbosity minimal
git diff --check
```
Record the exact pass/fail counts printed by each command (do not paraphrase - copy the "Passed!/Failed!" summary line). `dotnet test ... --no-build` after the unit-test run above already built the solution once; if a prior step used `--no-restore` only (not `--no-build`) for the first `dotnet test`, run a plain `dotnet build` (no filters) across the touched projects first so `--no-build` on the later commands has something to run against - or drop `--no-build` from the first `dotnet test` invocation only.

- [ ] **Step 2: Run the source scans from the task spec**

```
git diff --check
```
Then, using Grep (not manual reading) confirm:
- No `TenantId` property on `CreateDepartmentRequest`, `UpdateDepartmentRequest`, or `ListDepartmentsQuery` (covered by Task 8's new architecture test, but also grep the three files directly as a second check).
- No `HeadPositionId` accepted as an input parameter anywhere touched in this plan (query, controller params, command records) - `HeadPositionId` may still appear as an output-only property on response DTOs (`DepartmentListItemResponse`, `DepartmentResponse`, `DepartmentTreeNodeResponse`), which is correct and expected.
- No `Position` entity/API/schema file was modified: `git status --short` inside `HRMS-Backend-v1` should show no changes under `src/ONEVO.Domain/Features/OrgStructure/Position/`, `src/ONEVO.Api/Controllers/**/Position*`, or any new `*Position*` migration.
- No new file under `src/ONEVO.Infrastructure/Migrations/` was added (compare `git status --short` output for that directory against empty).
- No non-ASCII characters in any file touched by this plan - for each touched file, a quick check is: `grep -nP "[^\x00-\x7F]"` (or equivalent) returns nothing. Pay particular attention to the report file itself.

- [ ] **Step 3: Write DEPARTMENT_HARDENING_PART3_LIST_SEARCH_PAGINATION_REPORT.md**

Structure the report with these sections (fill in real content from Steps 1-2 and the work done in Tasks 1-9 - no placeholders):
- **Files read** - list every file read during research (repository, controller, handlers, DTOs, entity, config, existing tests, ValidationBehavior, ExceptionHandlerMiddleware).
- **Files changed** - full list, split into Created vs Modified, exactly matching Tasks 1-9's Files sections.
- **Route/query parameter table** - one row per query parameter (`search`, `includeInactive`, `parentDepartmentId`, `view`, `sortBy`, `sortDirection`, `page`, `pageSize`) with type, default, and validation rule.
- **Response shape** - both the flat JSON example and the tree JSON example (node shape with `children`), plus a note that `GET .../departments/{id}` (single department) was deliberately left unchanged - all required fields were already present in `DepartmentResponse`, and Part 2's `archive-check` endpoint already serves the dependency-summary need without adding a second query cost to every plain GET.
- **Repository filtering strategy** - explain `ListPageByLegalEntityAsync` and `ListForTreeByLegalEntityAsync` push tenantId/legalEntityId/includeInactive/search/parentDepartmentId/sort/skip/take into the EF query (`AsNoTracking`, no in-memory `ToList()` before filtering), and why `.ToLower().Contains(...)` was used instead of `EF.Functions.ILike` (InMemory-provider test compatibility).
- **Tree behavior decision** - restate: tree ignores `parentDepartmentId`/`page`/`pageSize` and applies `search`/`includeInactive` to the node set; departments whose parent falls outside the filtered set become roots. State this was tested in both a unit test (`DepartmentTreeMapperTests.BuildTree_TreatsDepartmentWithParentOutsideSet_AsRoot`) and a handler test (`ListDepartments_TreeView_IgnoresParentDepartmentIdAndPagination`).
- **Validation rules** - the exact FluentValidation rules from Task 5.
- **Tests added** - exact counts per project (unit, architecture, integration), split by file.
- **Verification results** - the actual pass/fail summary lines from Step 1's commands, and the actual result of `git diff --check`.
- **Known limitation** - Postgres orders NULLs last on ASC / first on DESC; EF InMemory (used by unit tests) orders NULLs first on ASC always - sort-by-Code and sort-by-UpdatedAt unit tests therefore only use non-null fixture values, so they do not exercise the two providers' differing null-ordering; this is a test-fidelity gap, not a functional bug in `ApplySort`.
- **Remaining gaps carried forward** - Position foundation still missing (Department-to-Position link remains unsupported), `head_position_id` assignment still deferred, frontend Department screen not implemented, Postman update not done.
- **Docker/integration status** - explicitly state whether Testcontainers actually started Postgres and the integration suite ran, or whether it failed at container startup and was skipped - do not claim tests passed if they did not run.
- **No commit or push was performed**, per the task's explicit instruction.

- [ ] **Step 4: Final read-through**

Read the report file back and confirm every claimed number (files changed, test counts, pass/fail) matches what Steps 1-2 actually produced - do not let optimistic rounding creep in (e.g. "all tests pass" when one project could not run due to missing Docker must instead say exactly that).

No commit - this is the last task in the plan. Report completion to the user with: exact files changed, exact test counts, whether Docker integration tests actually ran, any skipped verification, and confirmation that nothing was committed or pushed.
