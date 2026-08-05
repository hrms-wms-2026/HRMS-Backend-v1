# Department Hardening Part 2: Archive Dependency Checks and Restore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add archive-dependency checking (child departments, active employees, positions), a read-only archive-check endpoint, a restore endpoint, and dependency-blocked archive behavior to the existing Department backend in `HRMS-Backend-v1`, without touching Position APIs, headPositionId, or the frontend.

**Architecture:** Two new Application features (a query `CheckDepartmentArchiveDependencies` and a command `RestoreDepartment`) plus two new `IDepartmentRepository` counting methods, sharing dependency-evaluation logic through a new static `DepartmentArchiveDependencyEvaluator` so `ArchiveDepartmentCommandHandler` and the new check query never disagree about what blocks an archive. Two new controller actions (`POST .../archive-check`, `POST .../restore`) reuse the existing MediatR/Result/Problem pattern already used by every other Department endpoint.

**Tech Stack:** .NET (C#), MediatR, FluentValidation, EF Core (Npgsql + InMemory for tests), xUnit, Moq, FluentAssertions, Testcontainers.PostgreSql.

## Global Constraints

- Repo working directory: `C:\onevoNew\HRMS-Backend-v1`. Do not touch Position APIs, Postman, `OneVo-HR/` docs, or any frontend file.
- Never accept `tenantId`, `legalEntityId`, or `headPositionId` in a request body. `legalEntityId` comes from the route only.
- Never change `Department.HeadPositionId`, `Code`, or `Name` from the archive/restore/check code paths.
- Archive = `IsActive = false`. Restore = `IsActive = true`. Never physically delete a `departments` row.
- Use `IDateTimeProvider.UtcNow` for every `UpdatedAt` write -- never `DateTimeOffset.UtcNow` directly in the Department Application layer (enforced by `DepartmentPart2BArchitectureTests.DepartmentApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly`, which scans the whole `Department` folder including new files).
- `archive-check` requires `org:read`. `archive`, `restore`, and the legacy `DELETE` alias require `org:manage`.
- Repository reads use `.AsNoTracking()` and always take an explicit `tenantId` parameter (never rely on EF global query filters alone) -- matches `DepartmentPart2AArchitectureTests.IDepartmentRepository_EveryReadMethodHasATenantIdParameter`, which reflects over every `IDepartmentRepository` method automatically.
- Keep ASCII only in every file you write or edit.
- Do not commit or push. Do not run `git add`/`git commit`.
- No new abstractions beyond what's specified below -- do not build Position CRUD, employee-assignment APIs, or role/permission-management code.

**Key schema fact driving the Position decision (already verified by reading the code, do not re-derive):** `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs` has only `Name` and `DefaultRoleId` (plus `BaseEntity`'s `Id/TenantId/CreatedAt/UpdatedAt/CreatedById/IsDeleted/DeletedAt`). There is **no** `DepartmentId`, `LegalEntityId`, or status/active column on `Position`, and nothing links a `Position` row to a `Department` (only the reverse pointer `Department.HeadPositionId -> Position` exists, which is explicitly off-limits). This means "how many active positions belong to this department" is not just unmeasured, it is **structurally unrepresentable** in the current schema -- there is no column to query. Every task below therefore reports `activePositionCount = 0` with `positionDependencyCheckSupported = false` rather than inventing a count, per the task's own explicit fallback ("returned as 0 only if explicitly marked positionDependencyCheckSupported: false").

**Key schema fact driving the Employee "active" definition:** `Employee` (`src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs`) inherits `BaseEntity` (soft-delete via `IsDeleted`, already excluded by EF's automatic global query filter -- see `ApplicationDbContext.cs:259-262`) and has `DepartmentId`, `LegalEntityId`, and `EmploymentStatusId` (int FK to `Lookups.EmploymentStatus`, seeded rows `1=active, 2=on_leave, 3=suspended, 4=terminated` in `LookupDataSeeder.cs:72-78`). "Active employee" = joined to `EmploymentStatus` where `Code == "active"` (not the magic number `1`, so this stays correct even if seed IDs ever change) and scoped by `tenantId + legalEntityId + departmentId`.

**Key schema fact driving `GetByIdForLegalEntityAsync` reuse:** `EfDepartmentRepository.GetByIdForLegalEntityAsync` (already in the repo) has **no** `IsActive` filter -- it already returns archived rows. So no new "IncludingInactive" method is needed for restore; reuse this method exactly as-is. Likewise, no new "ParentIsActiveAsync" method is needed -- reuse the same method and check `.IsActive` on the returned entity, exactly like `CreateDepartmentCommandHandler`/`UpdateDepartmentCommandHandler` already do for parent checks.

---

## Task 1: Repository -- active-child and active-employee counting

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs`

**Interfaces:**
- Produces: `IDepartmentRepository.CountActiveChildrenAsync(Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default) : Task<int>`
- Produces: `IDepartmentRepository.CountActiveEmployeesAsync(Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default) : Task<int>`
- Consumed by: Task 3 (`DepartmentArchiveDependencyEvaluator`).

**Verification order matters here:** run `CountActiveEmployeesAsync_...` before assuming the query itself is correct. `Department` implements `ITenantOwnedEntity` directly (only the tenant global filter applies), but `Employee` inherits `BaseEntity`, which gets **both** the tenant filter and the unconditional `IsDeleted` filter composed together (`ApplicationDbContext.cs:259-262`). The fact that existing Department repository tests pass under `BuildInMemoryDb()`'s bare `Mock<ITenantContext>()` does not prove `Employee` queries behave identically. If the new employee test comes back with a count of `0` against correctly seeded rows, treat that as evidence the composed global filter is silently excluding rows (e.g. an unconfigured mock returning a tenant id that doesn't match), not as evidence the join/filter logic in the query itself is wrong -- debug the filter composition first.

- [ ] **Step 1: Add the two method signatures to `IDepartmentRepository`**

Add after the existing `IsDescendantAsync` method (before `AddAsync`):

```csharp
    Task<int> CountActiveChildrenAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default);

    Task<int> CountActiveEmployeesAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default);

```

- [ ] **Step 2: Implement both methods in `EfDepartmentRepository`**

Add after the existing `IsDescendantAsync` method (before `AddAsync`):

```csharp
    public async Task<int> CountActiveChildrenAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
    {
        var count = await _db.Departments
            .AsNoTracking()
            .Where(department =>
                department.TenantId == tenantId
                && department.LegalEntityId == legalEntityId
                && department.ParentDepartmentId == departmentId
                && department.IsActive)
            .CountAsync(ct);

        return count;
    }

    public async Task<int> CountActiveEmployeesAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
    {
        // "Active" means employment_statuses.code = "active" (not a hardcoded id), scoped
        // explicitly by tenant/legal-entity/department. BaseEntity's IsDeleted filter is
        // already applied automatically by the global query filter on Employees.
        var count = await (
            from employee in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking()
                on employee.EmploymentStatusId equals status.Id
            where employee.TenantId == tenantId
                && employee.LegalEntityId == legalEntityId
                && employee.DepartmentId == departmentId
                && status.Code == "active"
            select employee.Id)
            .CountAsync(ct);

        return count;
    }

```

- [ ] **Step 3: Add repository unit tests**

Append to `EfDepartmentRepositoryTests.cs`, before the closing brace of the class (before `private static ApplicationDbContext BuildInMemoryDb()`):

```csharp
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

```

- [ ] **Step 4: Run the unit test project**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` then `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: build succeeds, all tests pass including the two new ones.

---

## Task 2: Application DTOs and shared dependency evaluator

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentArchiveBlockers.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentArchiveDependencyResponse.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/Services/DepartmentArchiveDependencyEvaluator.cs`

**Interfaces:**
- Consumes: `IDepartmentRepository.CountActiveChildrenAsync`, `CountActiveEmployeesAsync` (Task 1).
- Produces: `DepartmentArchiveBlockers` record, `DepartmentArchiveDependencyResponse` record, `DepartmentArchiveDependencyEvaluator.EvaluateAsync(...)`, `.CanArchive(...)`, `.BuildMessage(...)` -- consumed by Task 3 (query handler) and Task 4 (archive handler).

- [ ] **Step 1: Create `DepartmentArchiveBlockers.cs`**

```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentArchiveBlockers(
    int ActiveSubdepartmentCount,
    int ActiveEmployeeCount,
    int ActivePositionCount,
    bool IsUsedAsParent,
    bool HasActiveEmployees,
    bool HasActivePositions,
    bool PositionDependencyCheckSupported);
```

- [ ] **Step 2: Create `DepartmentArchiveDependencyResponse.cs`**

```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentArchiveDependencyResponse(
    Guid DepartmentId,
    bool CanArchive,
    DepartmentArchiveBlockers Blockers,
    string Message);
```

- [ ] **Step 3: Create the shared evaluator**

```csharp
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Services;

// Position has no DepartmentId/LegalEntityId/status column yet (see Position.cs - Name and
// DefaultRoleId only), so there is no schema representation of "a position belongs to this
// department." ActivePositionCount is therefore always 0 and PositionDependencyCheckSupported
// is always false - a documented schema limitation, not an unverified guess: positions cannot
// be linked to a department at all today, so 0 is the only value that could ever be measured.
public static class DepartmentArchiveDependencyEvaluator
{
    public static async Task<DepartmentArchiveBlockers> EvaluateAsync(
        IDepartmentRepository departments,
        Guid tenantId,
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct)
    {
        var activeSubdepartmentCount = await departments.CountActiveChildrenAsync(
            tenantId, legalEntityId, departmentId, ct);
        var activeEmployeeCount = await departments.CountActiveEmployeesAsync(
            tenantId, legalEntityId, departmentId, ct);

        return new DepartmentArchiveBlockers(
            ActiveSubdepartmentCount: activeSubdepartmentCount,
            ActiveEmployeeCount: activeEmployeeCount,
            ActivePositionCount: 0,
            IsUsedAsParent: activeSubdepartmentCount > 0,
            HasActiveEmployees: activeEmployeeCount > 0,
            HasActivePositions: false,
            PositionDependencyCheckSupported: false);
    }

    public static bool CanArchive(DepartmentArchiveBlockers blockers)
    {
        return blockers.ActiveSubdepartmentCount == 0 && blockers.ActiveEmployeeCount == 0;
    }

    public static string BuildMessage(DepartmentArchiveBlockers blockers)
    {
        if (CanArchive(blockers))
        {
            return "No active employees, positions, or subdepartments are linked to this department.";
        }

        var reasons = new List<string>();
        if (blockers.ActiveSubdepartmentCount > 0)
        {
            reasons.Add("subdepartments");
        }
        if (blockers.ActiveEmployeeCount > 0)
        {
            reasons.Add("employees");
        }

        var joined = reasons.Count == 1 ? reasons[0] : string.Join(" and ", reasons);
        return $"This department cannot be archived yet. Reassign linked {joined} first.";
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: build succeeds (nothing references these types yet, but they must compile standalone).

---

## Task 3: `CheckDepartmentArchiveDependencies` query (the archive-check endpoint's backing feature)

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/Queries/CheckDepartmentArchiveDependencies/CheckDepartmentArchiveDependenciesQuery.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/Queries/CheckDepartmentArchiveDependencies/CheckDepartmentArchiveDependenciesQueryValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/Queries/CheckDepartmentArchiveDependencies/CheckDepartmentArchiveDependenciesQueryHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs`

**Interfaces:**
- Consumes: `IDepartmentRepository`, `ILegalEntityRepository`, `ICurrentUser` (existing), `DepartmentArchiveDependencyEvaluator` (Task 2).
- Produces: `CheckDepartmentArchiveDependenciesQuery(Guid LegalEntityId, Guid DepartmentId) : IRequest<Result<DepartmentArchiveDependencyResponse>>` -- consumed by Task 6 (controller).

- [ ] **Step 1: Create the query record**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;

public record CheckDepartmentArchiveDependenciesQuery(
    Guid LegalEntityId,
    Guid DepartmentId) : IRequest<Result<DepartmentArchiveDependencyResponse>>;
```

- [ ] **Step 2: Create the validator**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;

public class CheckDepartmentArchiveDependenciesQueryValidator
    : AbstractValidator<CheckDepartmentArchiveDependenciesQuery>
{
    public CheckDepartmentArchiveDependenciesQueryValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");
    }
}
```

- [ ] **Step 3: Create the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;

public class CheckDepartmentArchiveDependenciesQueryHandler
    : IRequestHandler<CheckDepartmentArchiveDependenciesQuery, Result<DepartmentArchiveDependencyResponse>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public CheckDepartmentArchiveDependenciesQueryHandler(
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<DepartmentArchiveDependencyResponse>> Handle(
        CheckDepartmentArchiveDependenciesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<DepartmentArchiveDependencyResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<DepartmentArchiveDependencyResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<DepartmentArchiveDependencyResponse>.NotFound("Legal entity not found.");

        var department = await _departments.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (department == null)
            return Result<DepartmentArchiveDependencyResponse>.NotFound("Department not found.");

        var blockers = await DepartmentArchiveDependencyEvaluator.EvaluateAsync(
            _departments, tenantId, request.LegalEntityId, department.Id, ct);

        var response = new DepartmentArchiveDependencyResponse(
            department.Id,
            DepartmentArchiveDependencyEvaluator.CanArchive(blockers),
            blockers,
            DepartmentArchiveDependencyEvaluator.BuildMessage(blockers));

        return Result<DepartmentArchiveDependencyResponse>.Success(response);
    }
}
```

- [ ] **Step 4: Add `using` statements to `DepartmentApplicationUnitTests.cs`**

Add near the top, alongside the existing `using ONEVO.Application.Features.OrgStructure.Queries.GetDepartment;` etc.:

```csharp
using ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;
```

- [ ] **Step 5: Add unit tests**

Add a new region after `#region GetDepartment ... #endregion` (or anywhere inside the class before the private helpers):

```csharp
    #region CheckDepartmentArchiveDependencies

    [Fact]
    public async Task CheckArchiveDependencies_ReturnsCanArchiveTrue_WhenAllCountsAreZero()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eligible");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new CheckDepartmentArchiveDependenciesQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            new CheckDepartmentArchiveDependenciesQuery(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CanArchive);
        Assert.Equal(0, result.Value.Blockers.ActiveSubdepartmentCount);
        Assert.Equal(0, result.Value.Blockers.ActiveEmployeeCount);
        Assert.Equal(0, result.Value.Blockers.ActivePositionCount);
        Assert.False(result.Value.Blockers.IsUsedAsParent);
        Assert.False(result.Value.Blockers.HasActiveEmployees);
        Assert.False(result.Value.Blockers.HasActivePositions);
        Assert.False(result.Value.Blockers.PositionDependencyCheckSupported);
        Assert.Equal(
            "No active employees, positions, or subdepartments are linked to this department.",
            result.Value.Message);
    }

    [Fact]
    public async Task CheckArchiveDependencies_ReturnsCanArchiveFalse_WithExactBlockerCounts()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Blocked");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var handler = new CheckDepartmentArchiveDependenciesQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            new CheckDepartmentArchiveDependenciesQuery(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanArchive);
        Assert.Equal(2, result.Value.Blockers.ActiveSubdepartmentCount);
        Assert.Equal(4, result.Value.Blockers.ActiveEmployeeCount);
        Assert.True(result.Value.Blockers.IsUsedAsParent);
        Assert.True(result.Value.Blockers.HasActiveEmployees);
        Assert.Equal(
            "This department cannot be archived yet. Reassign linked subdepartments and employees first.",
            result.Value.Message);
    }

    [Fact]
    public async Task CheckArchiveDependencies_ReturnsNotFound_WhenDepartmentDoesNotExist()
    {
        var missingDeptId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingDeptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new CheckDepartmentArchiveDependenciesQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            new CheckDepartmentArchiveDependenciesQuery(_legalEntityId, missingDeptId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    #endregion

```

- [ ] **Step 6: Run unit tests**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` then `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: build succeeds, all tests pass.

---

## Task 4: Make `ArchiveDepartmentCommandHandler` enforce the same dependency check

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/Department/Commands/ArchiveDepartment/ArchiveDepartmentCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs`

**Interfaces:**
- Consumes: `DepartmentArchiveDependencyEvaluator` (Task 2). No signature change to `ArchiveDepartmentCommand` itself -- the `DELETE` alias and `POST .../archive` both call this handler unchanged, so they inherit the new blocking behavior automatically.

**Regression check already done for this plan (do not redo, just be aware while editing):** every existing archive/delete call in `DepartmentsIntegrationTests.cs` (`Delete_WithOrgReadOnly_NoOrgManage_Returns403` L179, `Create_Get_Update_Delete_FullLifecycle` L220, `Update_ParentIsInactive_Returns409` L385-388, `Archive_Route_SoftDeactivates_AndListExcludesByDefault` L428) archives a department that has zero children and zero employees at the moment it's archived, so none of them will start returning 409 from this change. The only pre-existing unit test that archives successfully is `ArchiveDepartment_DeactivatesDepartmentRow_AndUsesInjectedClockForUpdatedAt` -- handled in Step 2 below.

- [ ] **Step 1: Add the evaluator check before deactivating**

In `ArchiveDepartmentCommandHandler.cs`, add `using ONEVO.Application.Features.OrgStructure.Services;` to the usings, then replace the body from `var existing = ...` through `return Result<bool>.Success(true);` with:

```csharp
        var existing = await _departments.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (existing == null)
            return Result<bool>.NotFound("Department not found.");

        var blockers = await DepartmentArchiveDependencyEvaluator.EvaluateAsync(
            _departments, tenantId, request.LegalEntityId, existing.Id, ct);
        if (!DepartmentArchiveDependencyEvaluator.CanArchive(blockers))
        {
            return Result<bool>.Conflict(DepartmentArchiveDependencyEvaluator.BuildMessage(blockers));
        }

        // Archive is a soft-deactivation, never a physical delete: audit history and
        // reporting-hierarchy references to this row remain intact.
        existing.IsActive = false;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _departments.Update(existing);
        await _departments.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
```

(Already-archived departments are unaffected by this change: if `existing.IsActive` is already `false` and there are no blockers, the handler still runs the same idempotent no-op it always has -- set `false` again, bump `UpdatedAt`, save. This preserves the pre-existing idempotent-archive convention rather than introducing a new conflict for repeat calls.)

- [ ] **Step 2: Update the existing archive-success unit test to mock zero blockers**

In `DepartmentApplicationUnitTests.cs`, find `ArchiveDepartment_DeactivatesDepartmentRow_AndUsesInjectedClockForUpdatedAt` and insert these two mock setups right after the existing `GetByIdForLegalEntityAsync` setup (before `var handler = new ArchiveDepartmentCommandHandler(...)`):

```csharp
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
```

- [ ] **Step 3: Add new blocked-archive unit tests**

Add inside `#region ArchiveDepartment`, after the existing two tests but before `#endregion`:

```csharp
    [Fact]
    public async Task ArchiveDepartment_Blocks_WhenActiveChildDepartmentsExist()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Has Children");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ArchiveDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchiveDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.True(existing.IsActive);
        _departmentRepoMock.Verify(d => d.Update(It.IsAny<Domain.Features.OrgStructure.Entities.Department>()), Times.Never);
        _departmentRepoMock.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchiveDepartment_Blocks_WhenActiveEmployeesExist()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Has Employees");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var handler = new ArchiveDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchiveDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.True(existing.IsActive);
    }

```

- [ ] **Step 4: Run unit tests**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` then `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: build succeeds, all tests pass (including the now-updated archive-success test).

---

## Task 5: `RestoreDepartment` command

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/Commands/RestoreDepartment/RestoreDepartmentCommand.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/Commands/RestoreDepartment/RestoreDepartmentCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Department/Commands/RestoreDepartment/RestoreDepartmentCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs`

**Interfaces:**
- Produces: `RestoreDepartmentCommand(Guid LegalEntityId, Guid DepartmentId) : IRequest<Result<bool>>` -- consumed by Task 6 (controller).

- [ ] **Step 1: Create the command record**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;

public record RestoreDepartmentCommand(
    Guid LegalEntityId,
    Guid DepartmentId) : IRequest<Result<bool>>;
```

- [ ] **Step 2: Create the validator**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;

public class RestoreDepartmentCommandValidator : AbstractValidator<RestoreDepartmentCommand>
{
    public RestoreDepartmentCommandValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");
    }
}
```

- [ ] **Step 3: Create the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;

public class RestoreDepartmentCommandHandler
    : IRequestHandler<RestoreDepartmentCommand, Result<bool>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RestoreDepartmentCommandHandler(
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<bool>> Handle(
        RestoreDepartmentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.NotFound("Legal entity not found.");

        // GetByIdForLegalEntityAsync has no IsActive filter, so this also finds
        // already-archived rows - required for restore to work at all.
        var existing = await _departments.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (existing == null)
            return Result<bool>.NotFound("Department not found.");

        if (existing.IsActive)
        {
            // Already active: idempotent success, matching ArchiveDepartmentCommandHandler's
            // existing precedent of not treating a repeat call as an error.
            return Result<bool>.Success(true);
        }

        if (existing.ParentDepartmentId is { } parentId)
        {
            var parent = await _departments.GetByIdForLegalEntityAsync(
                tenantId, request.LegalEntityId, parentId, ct);
            if (parent is null || !parent.IsActive)
            {
                return Result<bool>.Conflict(
                    "Cannot restore: the parent department is missing or inactive. Restore or reassign the parent first.");
            }
        }

        // Restore only flips IsActive. Children, HeadPositionId, code, and name are untouched.
        existing.IsActive = true;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _departments.Update(existing);
        await _departments.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
```

- [ ] **Step 4: Add `using` statement to `DepartmentApplicationUnitTests.cs`**

```csharp
using ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;
```

- [ ] **Step 5: Add unit tests**

Add a new region (e.g. after `#region ArchiveDepartment ... #endregion` and before `#region Tenant Context Isolation Guard`):

```csharp
    #region RestoreDepartment

    [Fact]
    public async Task RestoreDepartment_Succeeds_ForInactiveDepartmentWithNoParent_AndUsesInjectedClockForUpdatedAt()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived Root");
        existing.IsActive = false;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existing.IsActive);
        Assert.Equal(_fixedTime, existing.UpdatedAt);
        _departmentRepoMock.Verify(d => d.Update(existing), Times.Once);
        _departmentRepoMock.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreDepartment_DoesNotChangeHeadPositionId()
    {
        var headPositionId = Guid.NewGuid();
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived With Head");
        existing.IsActive = false;
        existing.HeadPositionId = headPositionId;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.Equal(headPositionId, existing.HeadPositionId);
    }

    [Fact]
    public async Task RestoreDepartment_Succeeds_ForInactiveDepartmentWithActiveParent()
    {
        var parent = CreateDepartment(_tenantId, _legalEntityId, "Active Parent");
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived Child");
        existing.IsActive = false;
        existing.ParentDepartmentId = parent.Id;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, parent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task RestoreDepartment_Rejects_WhenParentIsInactive()
    {
        var parent = CreateDepartment(_tenantId, _legalEntityId, "Inactive Parent");
        parent.IsActive = false;
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived Child");
        existing.IsActive = false;
        existing.ParentDepartmentId = parent.Id;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, parent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.False(existing.IsActive);
    }

    [Fact]
    public async Task RestoreDepartment_Rejects_WhenParentIsMissing()
    {
        var missingParentId = Guid.NewGuid();
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived Child");
        existing.IsActive = false;
        existing.ParentDepartmentId = missingParentId;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task RestoreDepartment_ReturnsNotFound_WhenDepartmentDoesNotExist()
    {
        var missingDeptId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingDeptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, missingDeptId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task RestoreDepartment_IsIdempotent_WhenAlreadyActive()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Already Active");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.Update(It.IsAny<Domain.Features.OrgStructure.Entities.Department>()), Times.Never);
    }

    [Fact]
    public async Task RestoreDepartment_ReturnsForbidden_WhenUnauthenticated()
    {
        var unauthenticatedUserMock = new Mock<ICurrentUser>();
        unauthenticatedUserMock.Setup(c => c.IsAuthenticated).Returns(false);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, unauthenticatedUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    #endregion

    #region CheckDepartmentArchiveDependencies Auth Guard

    [Fact]
    public async Task CheckArchiveDependencies_ReturnsForbidden_WhenUnauthenticated()
    {
        var unauthenticatedUserMock = new Mock<ICurrentUser>();
        unauthenticatedUserMock.Setup(c => c.IsAuthenticated).Returns(false);

        var handler = new CheckDepartmentArchiveDependenciesQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, unauthenticatedUserMock.Object);

        var result = await handler.Handle(
            new CheckDepartmentArchiveDependenciesQuery(_legalEntityId, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    #endregion

```

- [ ] **Step 6: Run unit tests**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` then `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: build succeeds, all tests pass.

---

## Task 6: Controller endpoints -- `archive-check` and `restore`

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs`

**Interfaces:**
- Consumes: `CheckDepartmentArchiveDependenciesQuery` (Task 3), `RestoreDepartmentCommand` (Task 5).
- Produces: `POST /api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}/archive-check` (org:read, 200 with `DepartmentArchiveDependencyResponse`), `POST /api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}/restore` (org:manage, 204).

- [ ] **Step 1: Add usings to the controller**

Add to `DepartmentsController.cs`, alongside the existing `using ONEVO.Application.Features.OrgStructure.Queries.GetDepartment;` etc.:

```csharp
using ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;
using ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;
```

- [ ] **Step 2: Add the two actions**

Add after the existing `Archive` method and before the `Delete` method (order doesn't matter functionally, but keeping archive-family actions together matches the file's existing grouping):

```csharp
    /// <summary>Checks whether a department can be archived: active subdepartments and
    /// active employees are counted from the database; active positions cannot be counted
    /// yet (Position has no DepartmentId column) and are reported as unsupported. Read-only -
    /// does not mutate the department.</summary>
    [HttpPost("{departmentId:guid}/archive-check")]
    [RequirePermission("org:read")]
    public async Task<IActionResult> ArchiveCheck(
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CheckDepartmentArchiveDependenciesQuery(legalEntityId, departmentId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Restores an archived department by setting IsActive = true. Returns 409 if
    /// the parent department is missing or inactive. Never touches children, HeadPositionId,
    /// code, or name.</summary>
    [HttpPost("{departmentId:guid}/restore")]
    [RequirePermission("org:manage")]
    public async Task<IActionResult> Restore(
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RestoreDepartmentCommand(legalEntityId, departmentId), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

```

- [ ] **Step 3: Add `using` statements to `DepartmentsControllerTests.cs`**

```csharp
using ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;
using ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;
```

- [ ] **Step 4: Add controller unit tests**

Add before the closing brace of `DepartmentsControllerTests.cs` (after the existing `Archive_NotFound_ReturnsProblem404` test, before `CreateAndUpdateRequests_DoNotExposeHeadPositionId`):

```csharp
    [Fact]
    public async Task ArchiveCheck_SendsQuery_WithRouteIds_AndReturnsOk()
    {
        var response = new DepartmentArchiveDependencyResponse(
            _departmentId, true,
            new DepartmentArchiveBlockers(0, 0, 0, false, false, false, false),
            "No active employees, positions, or subdepartments are linked to this department.");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CheckDepartmentArchiveDependenciesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentArchiveDependencyResponse>.Success(response));

        var result = await _sut.ArchiveCheck(_legalEntityId, _departmentId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CheckDepartmentArchiveDependenciesQuery>(q =>
                q.LegalEntityId == _legalEntityId && q.DepartmentId == _departmentId),
            It.IsAny<CancellationToken>()), Times.Once);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(response);
    }

    [Fact]
    public async Task ArchiveCheck_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CheckDepartmentArchiveDependenciesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentArchiveDependencyResponse>.NotFound("Department not found."));

        var result = await _sut.ArchiveCheck(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Restore_SendsCommand_WithRouteIds_AndReturnsNoContent()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RestoreDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _sut.Restore(_legalEntityId, _departmentId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<RestoreDepartmentCommand>(c => c.LegalEntityId == _legalEntityId && c.DepartmentId == _departmentId),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Restore_ParentInactive_ReturnsProblem409()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RestoreDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Conflict("Cannot restore: the parent department is missing or inactive. Restore or reassign the parent first."));

        var result = await _sut.Restore(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Restore_NotFound_ReturnsProblem404()
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RestoreDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.NotFound("Department not found."));

        var result = await _sut.Restore(_legalEntityId, _departmentId, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

```

- [ ] **Step 5: Run unit tests**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` then `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal`
Expected: build succeeds, all tests pass.

---

## Task 7: Architecture tests

**Files:**
- Modify: `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs`
- Modify: `tests/ONEVO.Tests.Architecture/DepartmentPart2BArchitectureTests.cs`
- Modify: `tests/ONEVO.Tests.Architecture/DepartmentsControllerArchitectureTests.cs`
- Create: `tests/ONEVO.Tests.Architecture/DepartmentPart2ArchiveRestoreArchitectureTests.cs`

- [ ] **Step 1: `DepartmentPart2AArchitectureTests.cs` -- add the two new repository methods to the legal-entity-scoping theory**

Add two `[InlineData(...)]` lines to the `IDepartmentRepository_LegalEntityScopedMethods_HaveALegalEntityIdParameter` theory (the `tenantId` blanket test already covers new methods automatically via reflection, no change needed there):

```csharp
    [InlineData(nameof(IDepartmentRepository.CountActiveChildrenAsync))]
    [InlineData(nameof(IDepartmentRepository.CountActiveEmployeesAsync))]
```

- [ ] **Step 2: `DepartmentPart2BArchitectureTests.cs` -- register the new command/query types and file locations**

Add usings:

```csharp
using ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;
using ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;
```

Update `CommandAndQueryTypes` and `HandlerTypes` arrays to include the new types:

```csharp
    private static readonly Type[] CommandAndQueryTypes =
    [
        typeof(CreateDepartmentCommand),
        typeof(UpdateDepartmentCommand),
        typeof(ArchiveDepartmentCommand),
        typeof(RestoreDepartmentCommand),
        typeof(ListDepartmentsQuery),
        typeof(GetDepartmentQuery),
        typeof(CheckDepartmentArchiveDependenciesQuery)
    ];

    private static readonly Type[] HandlerTypes =
    [
        typeof(CreateDepartmentCommandHandler),
        typeof(UpdateDepartmentCommandHandler),
        typeof(ArchiveDepartmentCommandHandler),
        typeof(RestoreDepartmentCommandHandler),
        typeof(ListDepartmentsQueryHandler),
        typeof(GetDepartmentQueryHandler),
        typeof(CheckDepartmentArchiveDependenciesQueryHandler)
    ];
```

Add new `[InlineData(...)]` lines to `CommandFiles_LiveUnderOrgStructureDepartmentFolder`:

```csharp
    [InlineData("Commands/RestoreDepartment", "RestoreDepartmentCommand.cs")]
    [InlineData("Commands/RestoreDepartment", "RestoreDepartmentCommandHandler.cs")]
    [InlineData("Commands/RestoreDepartment", "RestoreDepartmentCommandValidator.cs")]
```

Add new `[InlineData(...)]` lines to `QueryFiles_LiveUnderOrgStructureDepartmentFolder`:

```csharp
    [InlineData("Queries/CheckDepartmentArchiveDependencies", "CheckDepartmentArchiveDependenciesQuery.cs")]
    [InlineData("Queries/CheckDepartmentArchiveDependencies", "CheckDepartmentArchiveDependenciesQueryHandler.cs")]
    [InlineData("Queries/CheckDepartmentArchiveDependencies", "CheckDepartmentArchiveDependenciesQueryValidator.cs")]
```

(`Handlers_DoNotUseApplicationDbContextDirectly` and `DepartmentApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly` both run against these updated arrays / the whole folder automatically -- no further change needed, they will now also cover `RestoreDepartmentCommandHandler` and `CheckDepartmentArchiveDependenciesQueryHandler`.)

- [ ] **Step 3: `DepartmentsControllerArchitectureTests.cs` -- guard the two new routes**

Add after `ArchiveRoute_ExistsAsPost_WithOrgManagePermission`:

```csharp
    [Fact]
    public void ArchiveCheckRoute_ExistsAsPost_WithOrgReadPermission()
    {
        var method = ControllerType.GetMethod(nameof(DepartmentsController.ArchiveCheck));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("{departmentId:guid}/archive-check", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("org:read", GetPermission(method));
    }

    [Fact]
    public void RestoreRoute_ExistsAsPost_WithOrgManagePermission()
    {
        var method = ControllerType.GetMethod(nameof(DepartmentsController.Restore));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("{departmentId:guid}/restore", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("org:manage", GetPermission(method));
    }

```

- [ ] **Step 4: Create `DepartmentPart2ArchiveRestoreArchitectureTests.cs`**

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards Department Hardening Part 2 scope: archive dependency checks and restore.
/// No physical delete of departments, the new archive-check/restore files use
/// IDateTimeProvider instead of DateTimeOffset.UtcNow, no Position controller was added,
/// no role/permission-management code was added, and Delete/Archive both delegate to the
/// same ArchiveDepartmentCommand (so the DELETE alias inherits the new blocker check too).
/// </summary>
public sealed class DepartmentPart2ArchiveRestoreArchitectureTests
{
    [Fact]
    public void EfDepartmentRepository_NeverCallsRemoveOnDepartmentsDbSet()
    {
        var source = ReadRepositorySource();
        Assert.DoesNotContain("_db.Departments.Remove", source);
        Assert.DoesNotContain(".Remove(department", source);
    }

    [Fact]
    public void RestoreAndCheckArchiveHandlers_DoNotUseDateTimeOffsetUtcNowDirectly()
    {
        var deptAppRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Department");

        var targetFiles = new[]
        {
            Path.Combine(deptAppRoot, "Commands", "RestoreDepartment", "RestoreDepartmentCommandHandler.cs"),
            Path.Combine(deptAppRoot, "Commands", "ArchiveDepartment", "ArchiveDepartmentCommandHandler.cs"),
            Path.Combine(
                deptAppRoot, "Queries", "CheckDepartmentArchiveDependencies",
                "CheckDepartmentArchiveDependenciesQueryHandler.cs"),
        };

        foreach (var file in targetFiles)
        {
            Assert.True(File.Exists(file), $"expected {file} to exist");
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("DateTimeOffset.UtcNow", text);
        }
    }

    [Fact]
    public void NoPositionController_HasBeenAddedInPart2()
    {
        var apiAssembly = typeof(ONEVO.Api.Controllers.Tenant.OrgStructure.DepartmentsController).Assembly;
        var offender = apiAssembly.GetTypes()
            .FirstOrDefault(t => t.Name.Equals("PositionsController", StringComparison.OrdinalIgnoreCase));

        Assert.Null(offender);
    }

    [Fact]
    public void NoRoleOrPermissionManagementCode_WasAddedForThisFeature()
    {
        var deptAppRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Department");

        var newFolders = new[]
        {
            Path.Combine(deptAppRoot, "Commands", "RestoreDepartment"),
            Path.Combine(deptAppRoot, "Queries", "CheckDepartmentArchiveDependencies"),
        };

        foreach (var folder in newFolders)
        {
            Assert.True(Directory.Exists(folder), $"expected {folder} to exist");
            foreach (var file in Directory.GetFiles(folder, "*.cs"))
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("RoleTemplate", text);
                Assert.DoesNotContain("CreateRole", text);
                Assert.DoesNotContain("PermissionSeeder", text);
            }
        }
    }

    [Fact]
    public void DeleteAndArchiveActions_BothDelegateToArchiveDepartmentCommand()
    {
        var source = ReadControllerSource();
        var occurrences = Regex.Matches(source, @"new ArchiveDepartmentCommand\(").Count;
        Assert.Equal(2, occurrences);
    }

    private static string ReadControllerSource()
    {
        var path = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Api", "Controllers", "Tenant", "OrgStructure");
        return File.ReadAllText(Path.Combine(path, "DepartmentsController.cs"));
    }

    private static string ReadRepositorySource()
    {
        var path = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories", "OrgStructure", "Department");
        return File.ReadAllText(Path.Combine(path, "EfDepartmentRepository.cs"));
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

- [ ] **Step 5: Run architecture tests**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Expected: all tests pass.

---

## Task 8: Integration tests (only if Docker is available)

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs`

**Precondition:** Confirm Docker is available (`docker info` or equivalent) before starting this task, since this test class spins up a `PostgreSqlContainer` via Testcontainers unless `ONEVO_TEST_DB` is set. If Docker is not available, skip this task entirely and say so plainly in the report (do not fake results).

This task reuses the file's existing helpers exactly as they are today -- do not change their signatures: `CreateDepartmentAsync(TenantSession session, Guid legalEntityId, string name)`, `SendAsync(HttpMethod method, string host, string path, object? body, string? cookie = null, string? csrfToken = null, string? idempotencyKey = null)`, `GetJsonAsync(TenantSession session, string path)`, `ReadJsonAsync(HttpResponseMessage response)`, and the fixture fields `_tenantAOwner`, `_tenantAOrgReadOnly`, `_tenantAId`, `_tenantALegalEntityId`.

- [ ] **Step 1: Add `using Microsoft.EntityFrameworkCore;`-dependent employee seeding and new tests**

**On the two new unauthenticated tests below (`ArchiveCheck_Unauthenticated_Returns401`, `Restore_Unauthenticated_Returns401`):** these are POSTs sent with no cookie at all. This was verified against `CsrfProtectionMiddleware.cs` (not guessed): `ShouldValidate` only activates CSRF checking when an `onevo_session` cookie is present on the request (`CsrfProtectionMiddleware.cs:141`); with zero cookies, it returns `null` and the middleware calls `_next(context)` immediately, so the request reaches `RequirePermissionAttribute`, which returns `401 Unauthorized` for an unauthenticated `ICurrentUser`. If this ever changes (e.g. CSRF exemption logic is tightened), these two tests will fail loudly with a clear status-code mismatch rather than silently passing for the wrong reason -- if that happens, re-read `CsrfProtectionMiddleware.cs` before changing the assertion, do not just flip the expected code.

Add these `[Fact]` methods after the existing `HeadPositionId_IsIgnoredOnCreate_NotAcceptedFromRequestBody` test (the file already has `using Microsoft.EntityFrameworkCore;` and `using Microsoft.Extensions.DependencyInjection;` at the top):

```csharp
    [Fact]
    public async Task ArchiveCheck_Eligible_ReturnsCanArchiveTrue()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Check Eligible");
        var id = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive-check",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("canArchive").GetBoolean().Should().BeTrue();
        json.GetProperty("blockers").GetProperty("activeSubdepartmentCount").GetInt32().Should().Be(0);
        json.GetProperty("blockers").GetProperty("activeEmployeeCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ArchiveCheck_Unauthenticated_Returns401()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Check Unauth Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive-check", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Restore_Unauthenticated_Returns401()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Restore Unauth Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/restore", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ArchiveCheck_WithOrgRead_Returns200()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Check Perm Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive-check",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ArchiveCheck_Blocked_ReturnsAccurateCounts_WhenActiveChildExists()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Check Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Archive Check Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}/archive-check",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("canArchive").GetBoolean().Should().BeFalse();
        json.GetProperty("blockers").GetProperty("activeSubdepartmentCount").GetInt32().Should().Be(1);
        json.GetProperty("blockers").GetProperty("isUsedAsParent").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Archive_Blocked_WhenActiveChildExists_Returns409_AndDoesNotDeactivate()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Blocked Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Archive Blocked Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var afterArchive = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        var afterArchiveJson = await ReadJsonAsync(afterArchive);
        afterArchiveJson.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Delete_Blocked_WhenActiveChildExists_Returns409()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Delete Blocked Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Delete Blocked Child", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var delete = await SendAsync(HttpMethod.Delete, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_Child_WithNoBlockers_Succeeds_ThenRestore_Succeeds()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Archive Then Restore");
        var id = department.GetProperty("id").GetGuid();

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restore = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/restore",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await SendAsync(HttpMethod.Get, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}",
            body: null, cookie: _tenantAOwner.SessionCookie);
        var getJson = await ReadJsonAsync(get);
        getJson.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Restore_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Restore Perm Dept");
        var id = department.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);

        var response = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{id}/restore",
            body: null, cookie: _tenantAOrgReadOnly.SessionCookie, csrfToken: _tenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Restore_Fails_WhenParentIsArchived()
    {
        var parent = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Restore Parent Archived");
        var parentId = parent.GetProperty("id").GetGuid();
        var child = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments",
            new { name = "Restore Child Blocked", parentDepartmentId = parentId },
            cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var childJson = await ReadJsonAsync(child);
        var childId = childJson.GetProperty("id").GetGuid();

        // Archive child first (no blockers), then the parent (which now has zero active
        // children, so it archives cleanly too).
        await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{childId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var archiveParent = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{parentId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archiveParent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restoreChild = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{childId}/restore",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        restoreChild.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_Blocked_WhenActiveEmployeeExists()
    {
        var department = await CreateDepartmentAsync(_tenantAOwner, _tenantALegalEntityId, "Has Active Employee");
        var departmentId = department.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var activeStatus = await db.EmploymentStatuses.SingleAsync(s => s.Code == "active");

            db.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantAId,
                UserId = Guid.NewGuid(),
                LegalEntityId = _tenantALegalEntityId,
                DepartmentId = departmentId,
                EmployeeNumber = $"E{Guid.NewGuid():N}"[..12],
                FirstName = "Active",
                LastName = "Employee",
                Email = $"{Guid.NewGuid():N}@dept.test",
                EmploymentStatusId = activeStatus.Id,
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
            await db.SaveChangesAsync();
        }

        var archive = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}/archive",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var check = await SendAsync(HttpMethod.Post, _tenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_tenantALegalEntityId}/departments/{departmentId}/archive-check",
            body: null, cookie: _tenantAOwner.SessionCookie, csrfToken: _tenantAOwner.CsrfHeader);
        var checkJson = await ReadJsonAsync(check);
        checkJson.GetProperty("blockers").GetProperty("activeEmployeeCount").GetInt32().Should().Be(1);
        checkJson.GetProperty("blockers").GetProperty("hasActiveEmployees").GetBoolean().Should().BeTrue();
    }

```

- [ ] **Step 2: Run the Department integration tests**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` then `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~DepartmentsIntegrationTests" --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 10m`
Expected: all tests pass (existing 18 plus the ~11 new ones in this task).

---

## Task 9: Final full-suite verification and report

**Files:**
- Create: `DEPARTMENT_HARDENING_PART2_ARCHIVE_RESTORE_REPORT.md` (repo root, alongside the Part 1/2A/2B/2C/2D reports)

- [ ] **Step 1: Run the full verification suite**

Run each in order and record the exact output:

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
```

If Docker is available (see Task 8 precondition):

```
dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "FullyQualifiedName~Department" --verbosity minimal
```

```
git diff --check
```

Then an ASCII scan across every file touched in Tasks 1-8 (use the same locale-independent approach the Part 1 report used -- Part 1's own notes warn that `grep -qP` under Git Bash can silently no-op on a locale error and produce a false-clean result).

- [ ] **Step 2: Write `DEPARTMENT_HARDENING_PART2_ARCHIVE_RESTORE_REPORT.md`**

Must include, matching the structure of the Part 1/2A-2D reports already in the repo root:
- Files read (the list at the top of this plan's context, i.e. the 6 prior reports, the validation doc, and every file under "Inspect before editing" from the original task).
- Files changed (new/modified/deleted, matching Tasks 1-8 exactly).
- Exact `archive-check` / `archive` / `restore` routes and their permissions.
- Dependency-count sources: `CountActiveChildrenAsync` (Departments table, `ParentDepartmentId` + `IsActive`), `CountActiveEmployeesAsync` (Employees joined to EmploymentStatuses on `Code == "active"`, scoped by tenant/legal-entity/department).
- Explicit statement that `activePositionCount` is always `0` with `positionDependencyCheckSupported: false`, and why (Position has no DepartmentId/LegalEntityId/status column -- quote the exact fact from this plan's Global Constraints section).
- DELETE kept as compatibility alias (unchanged from Part 1) -- now also blocked by the same dependency check, proven by `Delete_Blocked_WhenActiveChildExists_Returns409` (Task 8) and `DeleteAndArchiveActions_BothDelegateToArchiveDepartmentCommand` (Task 7).
- Restore behavior: idempotent on already-active, 404 on missing department, 409 on missing/inactive parent, never touches children/HeadPositionId/code/name.
- Already-archived department re-archive behavior: idempotent no-op, consistent with the handler's pre-existing behavior (documented deviation-avoidance, not a new convention).
- Tests added/updated, with counts (repository: 2 new; application unit: ~14 new across check/archive-block/restore; controller unit: 6 new; architecture: ~7 new/updated across 4 files; integration: ~11 new if Docker was available, or explicitly state "skipped -- Docker unavailable" if not).
- Build/test results (paste the actual command output from Step 1).
- Remaining gaps (copy the list from the original task's "Report must include" section: search/sort/pagination, department details drawer, head-position assignment, Position foundation, management scope, occupant assignment, frontend states) plus explicitly restate the Position dependency-count schema limitation here too.

- [ ] **Step 3: Confirm no commit was made**

Run: `git status`
Expected: the new/modified files listed as untracked/modified; nothing staged or committed (per the "Do not commit or push" constraint).
