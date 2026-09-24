# Legal Entity Permissions & Accessible-Company Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the broad `org:manage` gate on Legal Entity create/update/delete with dedicated `legal_entity:create/update/delete` permissions, and replace "list every legal entity in the tenant" with an accessible-company filter (admin sees all, regular user sees only their own active employee's company).

**Architecture:** Three narrow permission codes are added to the existing catalog under the `org_structure` module (same module `org:read`/`org:manage` already live in), so a tenant's Owner role receives them automatically via the existing per-module entitlement mechanism — no new grant plumbing. `ListLegalEntitiesQueryHandler` stops calling a "list everything in tenant" repository method and instead calls one new repository method, `ListAccessibleAsync`, that branches inside a single EF query: management-permission holders get every (or every including inactive) legal entity in the tenant; everyone else gets at most one row, resolved via their own active `employees` row.

**Tech Stack:** .NET / EF Core / PostgreSQL, MediatR, xUnit + Moq + FluentAssertions, Testcontainers.PostgreSql for integration tests.

## Global Constraints

- Work only inside `C:\onevoNew\HRMS-Backend-v1`. Do not touch the frontend repo, OneVo-HR docs, Postman files, logo/upload/asset code, the countries table, employee-to-position assignment, or the multi-legal-entity authority model.
- Do not `git add`/commit/push. No commits in this plan's steps — the user runs verification and commits separately.
- `Employee.UserId` stays unique (one employee per user) — this plan relies on that, does not change it.
- Keep `org:read`/`org:manage` exactly as they are today (still required for Department/Position management and general org navigation) — only Legal Entity create/update/delete/general-settings move off `org:manage`.
- No endpoint may accept `tenantId`, `userId`, or `legalEntityId` from the request body/query to decide access — access is always derived from the authenticated session.
- **Known limitation to document, not fix:** `DefaultRoleSeeder` (production tenant provisioning path) grants permissions to a tenant's Owner role once, at tenant-creation time, from whatever is in the permission catalog then. Adding `legal_entity:*` to the catalog does **not** retroactively grant it to already-provisioned tenants' Owner roles — only tenants created after this change get it automatically. This plan does not build a backfill migration (out of scope); it must be called out explicitly in the final report as a deployment note. (Dev-box `DevSmokeTestTenantSeeder` is unaffected: its `SeedTenantRoleAsync` already does an idempotent additive backfill of missing `RolePermission` rows for existing roles on every restart, so the seeded Acme/Dapi Owners pick up the new permissions automatically next time the app starts.)

---

## File Structure

| File | Change |
|---|---|
| `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs` | Add 3 `Perm(...)` rows under `org_structure` |
| `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs` | Add 3 ownership rows under `org_structure` in `SeedPermissionOwnershipAsync` |
| `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs` | Remove `ListByTenantAsync`, add `ListAccessibleAsync` |
| `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs` | Remove `ListByTenantAsync` impl, add `ListAccessibleAsync` impl (branches admin-vs-employee, joins `Employees`/`EmploymentStatuses`) |
| `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/ListLegalEntities/ListLegalEntitiesQueryHandler.cs` | Compute `hasManagementAccess`, call `ListAccessibleAsync` |
| `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs` | Swap 5 `[RequirePermission]` attributes |
| `tests/ONEVO.Tests.Unit/Features/Auth/OrgPermissionSeedTests.cs` | Add ownership assertions for the 3 new permissions |
| `tests/ONEVO.Tests.Unit/Features/Tenancy/DefaultRoleSeederTests.cs` | Add a test proving Owner gets `legal_entity:*` when `org_structure` is subscribed |
| `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/ListLegalEntitiesQueryHandlerTests.cs` | Rewrite for `ListAccessibleAsync` mock, add accessibility-rule tests |
| `tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs` | Replace the single `org:manage` theory with per-endpoint expectations; add `userId`/request-record checks |
| `tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs` | Add an employee-aware fixture-user helper + accessible-company / permission-matrix tests |
| `HRMS-Backend-v1\LEGAL_ENTITY_PERMISSION_AND_ACCESS_FILTER_REPORT.md` | New report (root of `HRMS-Backend-v1`, matching sibling `*_REPORT.md` files) |

---

### Task 1: Permission catalog — add `legal_entity:create/update/delete`

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs:92-93`
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs:242-243`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/OrgPermissionSeedTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Tenancy/DefaultRoleSeederTests.cs`

**Interfaces:**
- Produces: permission codes `legal_entity:create`, `legal_entity:update`, `legal_entity:delete`, all with `Module = "org_structure"` — every later task's `[RequirePermission("legal_entity:...")]` attributes and `ICurrentUser.HasPermission("legal_entity:...")` checks depend on these codes existing exactly as spelled here.

- [ ] **Step 1: Write the failing tests**

Append to `OrgPermissionSeedTests.cs` (inside the existing class, after `ModuleCatalogSeeder_OwnsOrgReadAndOrgManage_UnderOrgStructureModule`):

```csharp
    [Fact]
    public async Task PermissionSeeder_SeedsLegalEntityPermissions_OwnedByOrgModule()
    {
        using var db = BuildInMemoryDb();
        var services = new ServiceCollection();
        services.AddScoped(_ => db);
        var sp = services.BuildServiceProvider();
        var seeder = new PermissionSeeder(sp, NullLogger<PermissionSeeder>.Instance);
        var method = typeof(PermissionSeeder).GetMethod(
            "SeedPermissionsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(seeder, [db, CancellationToken.None])!;

        var create = await db.Permissions.SingleAsync(p => p.Code == "legal_entity:create");
        var update = await db.Permissions.SingleAsync(p => p.Code == "legal_entity:update");
        var delete = await db.Permissions.SingleAsync(p => p.Code == "legal_entity:delete");

        create.Module.Should().Be("org_structure");
        update.Module.Should().Be("org_structure");
        delete.Module.Should().Be("org_structure");
    }

    [Fact]
    public async Task ModuleCatalogSeeder_OwnsLegalEntityPermissions_UnderOrgStructureModule()
    {
        using var db = BuildInMemoryDb();
        db.Permissions.AddRange(
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "legal_entity:create", Module = "org_structure" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "legal_entity:update", Module = "org_structure" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = Guid.NewGuid(), Code = "legal_entity:delete", Module = "org_structure" });
        await db.SaveChangesAsync();

        await ModuleCatalogSeeder.SeedAsync(db, CancellationToken.None);

        var ownerships = await db.ModulePermissionOwnerships.ToListAsync();

        ownerships.Should().Contain(o => o.ModuleKey == "org_structure" && o.PermissionCode == "legal_entity:create");
        ownerships.Should().Contain(o => o.ModuleKey == "org_structure" && o.PermissionCode == "legal_entity:update");
        ownerships.Should().Contain(o => o.ModuleKey == "org_structure" && o.PermissionCode == "legal_entity:delete");
    }
```

Append to `DefaultRoleSeederTests.cs`: extend `SeedOrgPermissionsAsync` to also add the three new rows, and add a dedicated grant test.

```csharp
    private static async Task SeedOrgPermissionsAsync(ApplicationDbContext db)
    {
        db.Permissions.AddRange(
            new Permission { Id = Guid.NewGuid(), Code = "org:read", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "org:manage", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "legal_entity:create", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "legal_entity:update", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "legal_entity:delete", Module = "org_structure" },
            new Permission { Id = Guid.NewGuid(), Code = "roles:read", Module = "roles" },
            new Permission { Id = Guid.NewGuid(), Code = "roles:manage", Module = "roles" },
            new Permission { Id = Guid.NewGuid(), Code = "settings:read", Module = "configuration" },
            new Permission { Id = Guid.NewGuid(), Code = "users:read", Module = "auth" },
            new Permission { Id = Guid.NewGuid(), Code = "notifications:manage", Module = "notifications" },
            new Permission { Id = Guid.NewGuid(), Code = "employees:read", Module = "core_hr" },
            new Permission { Id = Guid.NewGuid(), Code = "*", Module = "system" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SeedDefaultRolesAsync_GrantsLegalEntityPermissionsToOwner_WhenOrgStructureModuleIncluded()
    {
        using var db = BuildInMemoryDb();
        await SeedOrgPermissionsAsync(db);
        var seeder = new DefaultRoleSeeder(db, new ModuleEntitlementService(db));
        var tenantId = Guid.NewGuid();

        var owner = await seeder.SeedOwnerRoleAsync(tenantId, ["core_hr", "worksync_foundation", "org_structure"], CancellationToken.None);

        var grantedPermissionIds = owner.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        var legalEntityPermissionIds = await db.Permissions
            .Where(p => p.Code == "legal_entity:create" || p.Code == "legal_entity:update" || p.Code == "legal_entity:delete")
            .Select(p => p.Id)
            .ToListAsync();

        grantedPermissionIds.Should().Contain(legalEntityPermissionIds);
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~OrgPermissionSeedTests|FullyQualifiedName~DefaultRoleSeederTests" --no-restore --verbosity minimal`
Expected: FAIL — `legal_entity:create` etc. not found in `db.Permissions` / not owned by `org_structure`.

- [ ] **Step 3: Add the permissions to `PermissionSeeder.GetAllPermissions()`**

In `PermissionSeeder.cs`, right after the existing `org:manage` row (line 93):

```csharp
        Perm("org:manage", "Create and edit org structure, departments.", "org_structure"),
        Perm("legal_entity:create", "Create a legal entity (company) inside the tenant.", "org_structure"),
        Perm("legal_entity:update", "Edit a legal entity's general settings.", "org_structure"),
        Perm("legal_entity:delete", "Deactivate (soft-delete) a legal entity.", "org_structure"),
```

- [ ] **Step 4: Add the ownership rows to `ModuleCatalogSeeder.SeedPermissionOwnershipAsync`**

In `ModuleCatalogSeeder.cs`, right after the existing `org_structure` block (line 243):

```csharp
            // org_structure
            new { Module = "org_structure", Perm = "org:read" },
            new { Module = "org_structure", Perm = "org:manage" },
            new { Module = "org_structure", Perm = "legal_entity:create" },
            new { Module = "org_structure", Perm = "legal_entity:update" },
            new { Module = "org_structure", Perm = "legal_entity:delete" },
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~OrgPermissionSeedTests|FullyQualifiedName~DefaultRoleSeederTests" --no-restore --verbosity minimal`
Expected: PASS (all `OrgPermissionSeedTests` and `DefaultRoleSeederTests` tests green, including the pre-existing ones — `SeedOrgPermissionsAsync`'s new rows must not break the pre-existing assertions).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs tests/ONEVO.Tests.Unit/Features/Auth/OrgPermissionSeedTests.cs tests/ONEVO.Tests.Unit/Features/Tenancy/DefaultRoleSeederTests.cs
git commit -m "feat: add legal_entity:create/update/delete permissions under org_structure module"
```

---

### Task 2: Repository — replace `ListByTenantAsync` with `ListAccessibleAsync`

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs`

**Interfaces:**
- Consumes: `ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity` (existing, has `TenantId`, `Id`, `Name`, `IsActive`); `ApplicationDbContext.Employees` (has `TenantId`, `UserId`, `LegalEntityId`, `EmploymentStatusId`); `ApplicationDbContext.EmploymentStatuses` (has `Id`, `Code`).
- Produces: `Task<IReadOnlyList<LegalEntity>> ListAccessibleAsync(Guid tenantId, Guid userId, bool hasManagementAccess, bool includeInactive, CancellationToken ct = default)` — consumed by Task 3's `ListLegalEntitiesQueryHandler`.

- [ ] **Step 1: Update the interface**

In `ILegalEntityRepository.cs`, replace:

```csharp
    Task<IReadOnlyList<LegalEntity>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
```

with:

```csharp
    /// <summary>
    /// Returns the legal entities the given user may see: every (optionally including
    /// inactive) tenant legal entity when <paramref name="hasManagementAccess"/> is true,
    /// otherwise at most the single legal entity linked to the user's own active
    /// employees row. includeInactive is only honored on the management-access branch -
    /// a regular user's own company is only ever returned when it is active.
    /// </summary>
    Task<IReadOnlyList<LegalEntity>> ListAccessibleAsync(
        Guid tenantId, Guid userId, bool hasManagementAccess, bool includeInactive, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it in `EfLegalEntityRepository`**

Replace the `ListByTenantAsync` method (lines 19-28) with:

```csharp
    public async Task<IReadOnlyList<LegalEntity>> ListAccessibleAsync(
        Guid tenantId, Guid userId, bool hasManagementAccess, bool includeInactive, CancellationToken ct = default)
    {
        if (hasManagementAccess)
        {
            var query = _db.LegalEntities
                .AsNoTracking()
                .Where(entity => entity.TenantId == tenantId);

            if (!includeInactive)
                query = query.Where(entity => entity.IsActive);

            return await query.OrderBy(entity => entity.Name).ToListAsync(ct);
        }

        // Regular user: at most the one legal entity their own active employee row
        // points at. includeInactive is deliberately ignored here - a non-admin user
        // must never discover an archived company by flipping a query flag, and their
        // own company is only surfaced while it is active.
        var employeeLegalEntityId = await (
            from employee in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking()
                on employee.EmploymentStatusId equals status.Id
            where employee.TenantId == tenantId && employee.UserId == userId && status.Code == "active"
            select (Guid?)employee.LegalEntityId)
            .FirstOrDefaultAsync(ct);

        if (employeeLegalEntityId is null)
            return [];

        var own = await _db.LegalEntities
            .AsNoTracking()
            .Where(entity => entity.TenantId == tenantId && entity.Id == employeeLegalEntityId.Value && entity.IsActive)
            .FirstOrDefaultAsync(ct);

        return own is null ? [] : [own];
    }
```

- [ ] **Step 3: Build to confirm no dangling references**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: FAIL — `ListLegalEntitiesQueryHandler.cs` still calls the now-deleted `ListByTenantAsync`. This is expected; Task 3 fixes it. Note the error and proceed.

- [ ] **Step 4: Commit is deferred to the end of Task 3** (the build is intentionally red between these two tasks; do not commit mid-way).

---

### Task 3: `ListLegalEntitiesQueryHandler` — accessible-company filtering

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/ListLegalEntities/ListLegalEntitiesQueryHandler.cs`
- Modify (rewrite): `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/ListLegalEntitiesQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ILegalEntityRepository.ListAccessibleAsync(...)` from Task 2; `ICurrentUser.HasPermission(string)`, `ICurrentUser.UserId`, `ICurrentUser.TenantId`, `ICurrentUser.IsAuthenticated` (all pre-existing).
- Produces: same `Result<IReadOnlyList<LegalEntityListItemResponse>>` contract as before — no consumer outside this handler changes.

- [ ] **Step 1: Rewrite the test file (failing first)**

Replace the full contents of `ListLegalEntitiesQueryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class ListLegalEntitiesQueryHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ListLegalEntitiesQueryHandler BuildSut(bool hasUpdate = false, bool hasDelete = false)
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        _currentUser.Setup(c => c.HasPermission("legal_entity:update")).Returns(hasUpdate);
        _currentUser.Setup(c => c.HasPermission("legal_entity:delete")).Returns(hasDelete);
        return new ListLegalEntitiesQueryHandler(_legalEntities.Object, _currentUser.Object);
    }

    private static LegalEntityEntity Entity(string name, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        Name = name,
        CountryCode = "LKA",
        CurrencyCode = "LKR",
        IsActive = isActive
    };

    [Fact]
    public async Task Handle_ManagementAccess_ListsAllViaAccessibleQuery_WithHasManagementAccessTrue()
    {
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entity("Active Co", true)]);
        var sut = BuildSut(hasUpdate: true);

        var result = await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle().Which.Name.Should().Be("Active Co");
        _legalEntities.Verify(
            r => r.ListAccessibleAsync(TenantId, UserId, true, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DeletePermissionAlone_AlsoGrantsManagementAccess()
    {
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = BuildSut(hasDelete: true);

        await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        _legalEntities.Verify(
            r => r.ListAccessibleAsync(TenantId, UserId, true, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RegularUser_CallsAccessibleQuery_WithHasManagementAccessFalse()
    {
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entity("Own Co", true)]);
        var sut = BuildSut();

        var result = await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        result.Value!.Should().ContainSingle().Which.Name.Should().Be("Own Co");
        _legalEntities.Verify(
            r => r.ListAccessibleAsync(TenantId, UserId, false, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RegularUser_IncludeInactiveRequested_StillPassedThrough_ButFlaggedNonManagement()
    {
        // The repository is responsible for ignoring includeInactive on the non-management
        // branch; the handler's only job is to pass hasManagementAccess=false accurately.
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = BuildSut();

        var result = await sut.Handle(new ListLegalEntitiesQuery(IncludeInactive: true), CancellationToken.None);

        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RegularUserWithNoEmployee_ReturnsEmptyList()
    {
        _legalEntities.Setup(r => r.ListAccessibleAsync(TenantId, UserId, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = BuildSut();

        var result = await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);
        var sut = new ListLegalEntitiesQueryHandler(_legalEntities.Object, _currentUser.Object);

        var result = await sut.Handle(new ListLegalEntitiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ListLegalEntitiesQueryHandlerTests" --no-restore --verbosity minimal`
Expected: FAIL to compile — `ListAccessibleAsync` doesn't exist on the mocked interface yet from the handler's perspective (handler still calls old method), and handler ctor/behavior doesn't match.

- [ ] **Step 3: Rewrite the handler**

Replace the full contents of `ListLegalEntitiesQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;

public class ListLegalEntitiesQueryHandler
    : IRequestHandler<ListLegalEntitiesQuery, Result<IReadOnlyList<LegalEntityListItemResponse>>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public ListLegalEntitiesQueryHandler(ILegalEntityRepository legalEntities, ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<LegalEntityListItemResponse>>> Handle(
        ListLegalEntitiesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LegalEntityListItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<LegalEntityListItemResponse>>.Forbidden("Tenant context missing.");

        // "Management access" deliberately checks legal_entity:update/delete, not
        // org:manage - org:manage still gates Department/Position management and
        // general org navigation, but must not by itself unlock every company in the
        // tenant for the selector (see the permission-model rework this handler is
        // part of).
        var hasManagementAccess =
            _currentUser.HasPermission("legal_entity:update") || _currentUser.HasPermission("legal_entity:delete");

        var entities = await _legalEntities.ListAccessibleAsync(
            tenantId, _currentUser.UserId, hasManagementAccess, request.IncludeInactive, ct);

        var items = entities.Select(LegalEntityMapper.ToListItemResponse).ToList();

        return Result<IReadOnlyList<LegalEntityListItemResponse>>.Success(items);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ListLegalEntitiesQueryHandlerTests" --no-restore --verbosity minimal`
Expected: PASS.

- [ ] **Step 5: Build the API project to confirm Task 2 + 3 together compile**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: SUCCESS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/ListLegalEntities/ListLegalEntitiesQueryHandler.cs tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/ListLegalEntitiesQueryHandlerTests.cs
git commit -m "feat: filter legal entity list to accessible companies instead of every tenant row"
```

---

### Task 4: Controller — swap permission attributes

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`

**Interfaces:**
- Consumes: `legal_entity:create/update/delete` permission codes from Task 1.
- Produces: nothing new consumed elsewhere; Task 5's architecture tests assert against these exact attribute values.

- [ ] **Step 1: Edit the five attributes**

In `LegalEntitiesController.cs`:

- `GetGeneralSettings` (line 41): `[RequirePermission("org:manage")]` → `[RequirePermission("legal_entity:update")]`
- `Create` (line 52): `[RequirePermission("org:manage")]` → `[RequirePermission("legal_entity:create")]`
- `UpdateGeneralSettings` (line 74): `[RequirePermission("org:manage")]` → `[RequirePermission("legal_entity:update")]`
- `Delete` (line 111): `[RequirePermission("org:manage")]` → `[RequirePermission("legal_entity:delete")]`
- `RemoveLogo` (line 122): `[RequirePermission("org:manage")]` → `[RequirePermission("legal_entity:update")]`

`List` (line 30) stays `[RequirePermission("org:read")]` — unchanged.

- [ ] **Step 2: Build**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: SUCCESS (attribute strings are not compile-checked, so this just confirms nothing else broke).

- [ ] **Step 3: Commit is deferred to the end of Task 5** (architecture tests in Task 5 are the actual verification for this change; keep them in one commit).

---

### Task 5: Architecture tests — pin the new permission table

**Files:**
- Modify: `tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs`

**Interfaces:**
- Consumes: `LegalEntitiesController` attributes from Task 4; `CreateLegalEntityRequest`, `UpdateLegalEntityGeneralSettingsRequest`, `DeleteLegalEntityRequest` (existing records in `ONEVO.Api.Contracts.OrgStructure.LegalEntities`).

- [ ] **Step 1: Write the failing tests**

Replace the `MutatingAndDetailActions_UseOrgManage` theory (lines 64-76) and add new tests. Full replacement for that region:

```csharp
    [Fact]
    public void GetGeneralSettingsAction_UsesLegalEntityUpdate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.GetGeneralSettings));
        GetPermission(method!).Should().Be("legal_entity:update");
    }

    [Fact]
    public void CreateAction_UsesLegalEntityCreate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.Create));
        GetPermission(method!).Should().Be("legal_entity:create");
    }

    [Fact]
    public void UpdateGeneralSettingsAction_UsesLegalEntityUpdate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.UpdateGeneralSettings));
        GetPermission(method!).Should().Be("legal_entity:update");
    }

    [Fact]
    public void DeleteAction_UsesLegalEntityDelete()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.Delete));
        GetPermission(method!).Should().Be("legal_entity:delete");
    }

    [Fact]
    public void RemoveLogoAction_UsesLegalEntityUpdate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.RemoveLogo));
        GetPermission(method!).Should().Be("legal_entity:update");
    }

    [Fact]
    public void NoAction_UsesOrgManage()
    {
        var offenders = ActionMethods()
            .Select(GetPermission)
            .Where(p => p == "org:manage")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoAction_AcceptsUserIdParameter()
    {
        var offenders = ActionMethods()
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "userId", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData(typeof(ONEVO.Api.Contracts.OrgStructure.LegalEntities.CreateLegalEntityRequest))]
    [InlineData(typeof(ONEVO.Api.Contracts.OrgStructure.LegalEntities.UpdateLegalEntityGeneralSettingsRequest))]
    [InlineData(typeof(ONEVO.Api.Contracts.OrgStructure.LegalEntities.DeleteLegalEntityRequest))]
    public void RequestContracts_DoNotAcceptTenantIdOrUserId(Type requestType)
    {
        var offenders = requestType.GetProperties()
            .Where(p =>
                string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, "userId", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(offenders);
    }
```

Note: `FluentAssertions` is not currently imported in this file (it uses raw `Assert.*`). Add `using FluentAssertions;` and `using ONEVO.Api.Contracts.OrgStructure.LegalEntities;` (or keep fully-qualified names as written above — either is fine, the snippet above uses fully-qualified names in the `[InlineData]` so no new using is strictly required there, but `Should().Be(...)` in the other tests does need the `FluentAssertions` using).

Also update the existing `NoAction_AcceptsTenantIdParameter` test's name/comment is unaffected — it stays as-is; the new `NoAction_AcceptsUserIdParameter` is additive per the plan spec's "no request contract accepts tenantId or userId" requirement, covering the parameter surface, while `RequestContracts_DoNotAcceptTenantIdOrUserId` covers the DTO surface.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --filter "FullyQualifiedName~LegalEntitiesControllerArchitectureTests" --no-restore --verbosity minimal`
Expected: FAIL — `GetGeneralSettingsAction_UsesLegalEntityUpdate` etc. fail because Task 4 hasn't landed yet in this ordering, OR if Task 4 already landed, this step trivially passes and step 2/3 collapse — since Task 4 already changed the controller, run this after Task 4's edit is in place (it is, per this plan's ordering) and expect PASS. If for any reason Task 4 was skipped, this is the red step.

- [ ] **Step 3: Confirm pass (attributes already changed in Task 4)**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --filter "FullyQualifiedName~LegalEntitiesControllerArchitectureTests" --no-restore --verbosity minimal`
Expected: PASS — all tests in the file green, including the untouched ones (`Controller_RequiresTenantPolicy`, `ListAction_UsesOrgRead`, `NoPutLogoRoute_Exists`, etc.).

- [ ] **Step 4: Commit (covers Task 4 + Task 5 together)**

```bash
git add src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs
git commit -m "feat: gate legal entity create/update/delete on dedicated permissions instead of org:manage"
```

---

### Task 6: Integration tests — accessible-company matrix over real Postgres

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs`

**Interfaces:**
- Consumes: `TenantSession`, `SendAsync`, `GetJsonAsync`, `ReadJsonAsync`, `ParseSetCookies`, `LoginViaBaseHostAsync`-equivalent flow, `ProvisionAndLoginOwnerAsync`, `GetPrimaryLegalEntityIdAsync`, `CreateCompanyAsync` — all pre-existing in this file.
- Produces: a new private helper `SeedAndLoginEmployeeFixtureUserAsync` used only within this file.

- [ ] **Step 1: Add the fixture-user-with-employee helper and required usings**

Add these usings at the top of the file (alongside the existing ones):

```csharp
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
```

Add this private method near `GetPrimaryLegalEntityIdAsync` (it mirrors `DepartmentsIntegrationTests.SeedAndLoginFixtureUserAsync` but also inserts an `Employee` row so the fixture user has an accessible company):

```csharp
    private const string FixtureUserPassword = "Password123!";

    private async Task<TenantSession> SeedAndLoginEmployeeFixtureUserAsync(
        Guid tenantId, string host, string email, IReadOnlyList<string> permissionCodes,
        string roleName, Guid legalEntityId, string employeeNumber)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTimeOffset.UtcNow;

        var userId = Guid.NewGuid();
        db.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Email = email,
            FirstName = "Fixture",
            LastName = roleName,
            PasswordHash = hasher.Hash(FixtureUserPassword),
            IsActive = true,
            EmailVerified = true,
            MustChangePassword = false,
            PasswordSetByAdmin = false,
            CreatedAt = now,
            CreatedById = userId
        });

        var roleId = Guid.NewGuid();
        db.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Name = roleName,
            Description = $"Legal entity fixture role: {roleName}",
            IsSystem = false,
            CreatedAt = now,
            CreatedById = userId
        });

        foreach (var code in permissionCodes)
        {
            var permission = await db.Permissions.SingleAsync(p => p.Code == code);
            db.Add(new RolePermission { TenantId = tenantId, RoleId = roleId, PermissionId = permission.Id });
        }

        db.Add(new UserRole { TenantId = tenantId, UserId = userId, RoleId = roleId, AssignedAt = now, AssignedBy = userId });

        db.Add(new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            EmployeeNumber = employeeNumber,
            FirstName = "Fixture",
            LastName = roleName,
            Email = email,
            LegalEntityId = legalEntityId,
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1),
            CreatedAt = now,
            CreatedById = userId
        });

        db.Add(new LegalAcceptanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
            DocumentType = "terms", DocumentVersion = "1.0", Decision = "accepted",
            Required = true, DecidedAt = now, Source = "test-seed"
        });
        db.Add(new LegalAcceptanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
            DocumentType = "privacy_notice", DocumentVersion = "1.0", Decision = "acknowledged",
            Required = true, DecidedAt = now, Source = "test-seed"
        });

        await db.SaveChangesAsync();

        const string baseHost = "localhost";
        var loginResponse = await SendAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email, password = FixtureUserPassword });
        var loginJson = await ReadJsonAsync(loginResponse);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, loginJson.ToString());
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange",
            new { code = exchangeCode });
        var exchangeJson = await ReadJsonAsync(exchangeResponse);
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, exchangeJson.ToString());
        var cookies = ParseSetCookies(exchangeResponse);

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}; onevo_csrf={cookies["onevo_csrf"]}";
        var csrfHeader = Uri.UnescapeDataString(cookies["onevo_csrf"]);
        return new TenantSession(host, sessionCookie, csrfHeader);
    }
```

- [ ] **Step 2: Extend `InitializeAsync` to provision two fixture users on tenant A**

In `InitializeAsync`, after `_tenantASecondLegalEntityId`-equivalent setup (there isn't one yet in this file — add it) and the existing `_tenantAPrimaryLegalEntityId`/`_tenantBPrimaryLegalEntityId` lines, add:

```csharp
        _tenantAId = await GetTenantIdAsync(_tenantA.Host);
        _tenantASecondLegalEntityId = await CreateSecondLegalEntityForFixturesAsync(_tenantA);

        _tenantAManager = await SeedAndLoginEmployeeFixtureUserAsync(
            _tenantAId, _tenantA.Host, "manager@legal-ent-a.test",
            permissionCodes: ["org:read", "org:manage", "legal_entity:update", "legal_entity:delete"],
            roleName: "Legal Entity Manager", legalEntityId: _tenantAPrimaryLegalEntityId, employeeNumber: "FIX-MGR-001");

        _tenantARegularEmployee = await SeedAndLoginEmployeeFixtureUserAsync(
            _tenantAId, _tenantA.Host, "regular@legal-ent-a.test",
            permissionCodes: ["org:read", "org:manage"],
            roleName: "Regular Employee", legalEntityId: _tenantASecondLegalEntityId, employeeNumber: "FIX-REG-001");
```

Add the matching fields near the other private fields:

```csharp
    private TenantSession _tenantAManager = null!;
    private TenantSession _tenantARegularEmployee = null!;
    private Guid _tenantAId;
    private Guid _tenantASecondLegalEntityId;
```

Add the two small helpers this needs. `GetTenantIdAsync` is copied verbatim from `DepartmentsIntegrationTests.cs:1115-1122` (resolves the tenant id directly from the DB by slug — no admin API round trip), and `Tenant` is already reachable via this file's existing `using ONEVO.Domain.Features.InfrastructureModule.Entities;`:

```csharp
    private async Task<Guid> GetTenantIdAsync(string host)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var slug = host.Split('.')[0];
        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Slug == slug);
        return tenant.Id;
    }

    private async Task<Guid> CreateSecondLegalEntityForFixturesAsync(TenantSession owner)
    {
        var company = await CreateCompanyAsync(owner, "Fixture Second Co", "FIXSEC1", "REG-FIXSEC1");
        return company.Id;
    }
```

- [ ] **Step 3: Write the failing accessibility tests**

Append to the class, in a new `// ── 8. Accessible-company filtering ──` region:

```csharp
    [Fact]
    public async Task List_Owner_SeesAllActiveCompaniesInTenant()
    {
        var list = await GetJsonAsync(_tenantA, "/api/v1/org/legal-entities");
        var ids = list.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(_tenantAPrimaryLegalEntityId);
        ids.Should().Contain(_tenantASecondLegalEntityId);
    }

    [Fact]
    public async Task List_ManagerWithLegalEntityUpdate_SeesAllActiveCompaniesInTenant()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host, "/api/v1/org/legal-entities",
            body: null, cookie: _tenantAManager.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var ids = json.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(_tenantAPrimaryLegalEntityId);
        ids.Should().Contain(_tenantASecondLegalEntityId);
    }

    [Fact]
    public async Task List_RegularEmployee_SeesOnlyOwnLegalEntity()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host, "/api/v1/org/legal-entities",
            body: null, cookie: _tenantARegularEmployee.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var ids = json.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().ContainSingle().Which.Should().Be(_tenantASecondLegalEntityId);
    }

    [Fact]
    public async Task List_RegularEmployee_IncludeInactiveTrue_StillOnlyOwnActiveCompany()
    {
        var response = await SendAsync(HttpMethod.Get, _tenantA.Host, "/api/v1/org/legal-entities?includeInactive=true",
            body: null, cookie: _tenantARegularEmployee.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        var ids = json.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().ContainSingle().Which.Should().Be(_tenantASecondLegalEntityId);
    }

    [Fact]
    public async Task RegularEmployee_CannotUpdateOrDeleteLegalEntity_WithoutPermission()
    {
        var updateResponse = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/general-settings",
            UpdateBody("Should Not Apply", "FIXSEC1", "REG-FIXSEC1", [1, 2, 3, 4, 5]),
            cookie: _tenantARegularEmployee.SessionCookie, csrfToken: _tenantARegularEmployee.CsrfHeader);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteResponse = await SendAsync(HttpMethod.Delete, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}",
            new { confirmName = "Fixture Second Co" },
            cookie: _tenantARegularEmployee.SessionCookie, csrfToken: _tenantARegularEmployee.CsrfHeader);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ManagerWithLegalEntityUpdate_CanUpdate_ButNotDeleteWithoutLegalEntityDelete()
    {
        // _tenantAManager was seeded with both legal_entity:update and legal_entity:delete
        // above, so this test also proves Create still requires legal_entity:create
        // specifically - the manager fixture intentionally omits it.
        var createResponse = await SendAsync(HttpMethod.Post, _tenantA.Host, "/api/v1/org/legal-entities",
            CreateCompanyBody("Manager Create Attempt Co", "MGRCA1", "REG-MGRCA1"),
            cookie: _tenantAManager.SessionCookie, csrfToken: _tenantAManager.CsrfHeader);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updateResponse = await SendAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{_tenantASecondLegalEntityId}/general-settings",
            UpdateBody("Fixture Second Co Renamed", "FIXSEC1", "REG-FIXSEC1", [1, 2, 3, 4, 5]),
            cookie: _tenantAManager.SessionCookie, csrfToken: _tenantAManager.CsrfHeader);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
```

- [ ] **Step 4: Run to verify failure (or first successful run, since Postgres/Testcontainers is required)**

Run (only if Docker is available, or `ONEVO_TEST_DB` is set to a local PostgreSQL): `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~LegalEntitiesIntegrationTests" --no-restore --verbosity minimal`
Expected: FAIL before Tasks 1-5 land (permission codes / repository method don't exist yet); since this plan executes Task 6 last, expect PASS on first run once compiled against the finished Tasks 1-5 code — if any test is red, read the failure message before changing anything (see systematic-debugging skill) rather than loosening an assertion.

- [ ] **Step 5: Fix forward until green, then run the full integration file**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~LegalEntitiesIntegrationTests" --no-restore --verbosity minimal`
Expected: PASS — all tests in the file, including the pre-existing ones (Owner-only flows must be untouched by this change).

- [ ] **Step 6: Commit**

```bash
git add tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs
git commit -m "test: cover accessible-company filtering and legal_entity:* permission matrix over real Postgres"
```

---

### Task 7: Full verification sweep + report

**Files:**
- Create: `HRMS-Backend-v1\LEGAL_ENTITY_PERMISSION_AND_ACCESS_FILTER_REPORT.md`

**Interfaces:**
- Consumes: results of every prior task's test run.

- [ ] **Step 1: Run the full required verification commands, in order**

```bash
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal
dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~LegalEntity|FullyQualifiedName~Auth|FullyQualifiedName~DevSmoke" --no-restore --verbosity minimal
git diff --check
```

Record the pass/fail counts and any pre-existing unrelated failures verbatim (do not silently absorb them into "all green" if they were already failing before this change — check with `git stash` + re-run only if a failure looks suspicious, per the systematic-debugging skill, rather than assuming).

- [ ] **Step 2: Write the report**

Create `HRMS-Backend-v1\LEGAL_ENTITY_PERMISSION_AND_ACCESS_FILTER_REPORT.md` with these sections (fill every value from the actual code/test-run output, not from this plan's draft numbers):

- **Permissions added** — exact codes (`legal_entity:create`, `legal_entity:update`, `legal_entity:delete`), module (`org_structure`), descriptions used.
- **Seeded roles changed** — state plainly that no seed *code* change was needed for the Owner grant (it falls out of `GetEntitledPermissionsAsync` once the permissions exist under `org_structure`), cite the new `DefaultRoleSeederTests` test as proof, and state that HR Manager/Work Manager dev-smoke roles were deliberately left untouched (their explicit permission-code lists don't include the new codes).
- **Before/after endpoint permission table** — the 6 `LegalEntitiesController` actions, old permission → new permission.
- **Accessible-company filtering rule** — the exact branching logic from `ListAccessibleAsync` (management access vs. active-employee lookup), including that `includeInactive` is ignored for non-management users.
- **Current limitation** — one `Employee` per `User` (via unique `Employee.UserId`), so a regular user can access exactly one legal entity today.
- **Deployment note (from the Global Constraints backfill gap)** — state explicitly that already-provisioned tenants' Owner roles will not retroactively receive `legal_entity:*` until/unless a backfill is run; only tenants created after this change get it via normal provisioning. Dev-box `DevSmokeTestTenantSeeder` self-heals on next restart.
- **Future follow-up** — extending the accessible-company query with `position_assignments`/multi-company authority once that model exists, to support one user legitimately spanning multiple legal entities.
- **Verification results** — the actual output/counts from Step 1.
- **Files changed** — the full list from the File Structure table above.
- **Explicit statement** that no frontend, Postman, OneVo-HR docs, logo/upload, or country-table work was touched.

- [ ] **Step 3: Final `git status` sanity check (no commit)**

Run: `git status`
Confirm only the files listed in the report's "Files changed" section (plus the new report and plan doc) show as modified/untracked. Do not stage or commit — the user handles that.
