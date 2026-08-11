# Work Management — Milestone (Objective) Hierarchy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `projects:access`/`projects:read` permission retrofit, `Objective.ReportingManagerId` + `objective_change_requests` schema, the fully-hardcoded tree-authorization rule, and 8 endpoints (create/edit/delete/transfer a sub-milestone, approve/reject/list change requests, get the full Objective tree) per `docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md`.

**Architecture:** Same ASP.NET Core / CQRS-via-MediatR / EF Core (Npgsql/PostgreSQL) stack as Foundation and Slice 2. One new migration (new table + one new column). New `IObjectiveChangeRequestRepository`; `IObjectiveRepository` gains read/update methods. Authorization is entirely tree-position checks in handlers — no new permission codes beyond the two retrofitted ones.

**Tech Stack:** .NET / ASP.NET Core, EF Core (Npgsql), PostgreSQL, MediatR, FluentValidation, xUnit + Moq (unit), xUnit + Testcontainers (integration), `dotnet test`.

## Global Constraints

- Domain must not reference Application/Infrastructure/API/EF Core. Application must not reference Infrastructure or `HttpContext`.
- Every async method takes `CancellationToken`, is awaited; no `.Result`/`.Wait()`.
- Validation via MediatR `ValidationBehavior` (FluentValidation) only.
- `Result`/`Result<T>` exactly as `src/ONEVO.Application/Common/Models/Result.cs` defines — controllers use `result.IsSuccess ? Ok(...) : Problem(result.Error, statusCode: result.StatusCode ?? 400)`.
- `tenantId`/`userId` always resolved from `ICurrentUser` inside handlers, never trusted from the request body.
- **The Default Objective is excluded from Edit/Delete/Transfer (endpoints in Tasks 6-8)** — `400` if `{id}` resolves to one (design §5 carve-out). It is edited/deleted only as a side effect of the Project-level endpoints (Slice 2, unchanged by this plan).
- **Conflict rule (design §8, user-confirmed):** an Objective edit conflicts with its parent when the new `[StartDate, EndDate]` falls outside the parent's `[StartDate, EndDate]` (inclusive bounds — not a conflict) **or** the new `AllocatedHours` exceeds the parent's `AllocatedHours` (compared against the parent's total, not remaining headroom after siblings). Either dimension failing is one combined conflict, not two.
- **Creation is validated against the same conflict rule** (implementation-level decision, not previously stated in the design — a sub-milestone must not be created already out of its parent's bounds; `400` if so, not silently allowed then requiring an edit-approval to fix later).
- Raw SQL is forbidden except migration RLS-policy SQL and the one `role_permissions` data-migration SQL in Task 1, following the exact pattern in `20260729082336_AddTenantSessionExchangeChallenges.cs`.
- Permission codes: `projects:access` (module base gate, all 8 endpoints require it) and `projects:read` (unused by this plan — no cross-user "view others" concept for Objectives; the tree-visibility endpoint, #8/Task 12, uses membership fallback like `GetById` in Slice 2, not `projects:read`).
- **Dependency: `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md` (Slice 2) must be executed before this plan.** Task 11 (`GetObjectiveTreeQueryHandler`) consumes `IProjectRepository.GetByIdForTenantAsync` and `IProjectMemberRepository.HasActiveMembershipAsync`, both added by Slice 2's Task 1 — they do not exist in the currently-shipped Foundation code. Task 13's integration tests also assume Slice 2's Edit/Delete/GetById/List `[Fact]`s and their private HTTP helpers already exist in `CreateProjectEndpointTests.cs` by the time this plan's tests are added to the same file.

---

### Task 1: `PermissionSeeder.cs` retrofit — `projects:access` + retire unused/renamed codes

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_RetireLegacyWorkManagementPermissions.cs` (generated, not hand-written)

**Interfaces:**
- Produces: `projects:access` permission row, seeded and available to every `[RequirePermission("projects:access")]` attribute added by this plan and by the (already-written, not-yet-executed) Slice 2 plan's Task 7.

This is a prerequisite for every other task in this plan and for Slice 2's Task 7 — both use `[RequirePermission("projects:access")]`, which resolves to nothing at runtime until this seed exists.

- [ ] **Step 1: Update `GetAllPermissions()`**

In `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`, replace the two `// Projects` and `// Work Management — Projects (Foundation slice additions)` blocks:

```csharp
        // Projects
        Perm("projects:read", "View projects.", "work_management"),
        Perm("projects:access", "Work Management module access — create/edit/delete your own projects and milestones.", "work_management"),

        // Work Management — Projects (Foundation slice additions)
        // (members:read, members:manage, invitations:manage, invitations:respond, versions:write,
        // labels:manage retired 2026-08-04 - collapsed into projects:access per the milestone-hierarchy
        // design's "multiple features mapped onto a single permission" decision. They were seeded
        // ahead of any endpoint using them and are removed before any handler ever checked them.)
```

Remove the `Perm("projects:write", ...)`, `Perm("projects:create", ...)` lines and the six `members:*`/`invitations:*`/`versions:write`/`labels:manage` lines entirely — do not leave them commented out as dead code, the comment above documents the removal.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors (removing `Perm(...)` calls is source-only, no migration needed for the seeder logic itself — `SeedPermissionsAsync` already handles rows disappearing from `GetAllPermissions()` by simply no longer re-adding them; it does not delete existing rows).

- [ ] **Step 3: Generate the data-migration**

Run: `dotnet ef migrations add RetireLegacyWorkManagementPermissions --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

This generates an (likely empty, since no `IEntityTypeConfiguration` changed) migration scaffold — open it and replace the body with hand-written data-migration SQL in `Up`:

```csharp
public partial class RetireLegacyWorkManagementPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Reassign every role_permissions row referencing a retired code to projects:access
        // instead, deduplicating if the role already has both (ON CONFLICT DO NOTHING avoids a
        // unique-constraint violation on (role_id, permission_id) when a role already holds
        // projects:access as well as one of the retired codes).
        migrationBuilder.Sql(@"
            INSERT INTO role_permissions (id, tenant_id, role_id, permission_id, created_at)
            SELECT gen_random_uuid(), rp.tenant_id, rp.role_id, p_new.id, now()
            FROM role_permissions rp
            JOIN permissions p_old ON p_old.id = rp.permission_id
            JOIN permissions p_new ON p_new.code = 'projects:access'
            WHERE p_old.code IN ('projects:write', 'projects:create',
                                  'members:read', 'members:manage',
                                  'invitations:manage', 'invitations:respond',
                                  'versions:write', 'labels:manage')
            ON CONFLICT (role_id, permission_id) DO NOTHING;

            DELETE FROM role_permissions
            WHERE permission_id IN (
                SELECT id FROM permissions WHERE code IN (
                    'projects:write', 'projects:create',
                    'members:read', 'members:manage',
                    'invitations:manage', 'invitations:respond',
                    'versions:write', 'labels:manage')
            );

            DELETE FROM permissions
            WHERE code IN ('projects:write', 'projects:create',
                            'members:read', 'members:manage',
                            'invitations:manage', 'invitations:respond',
                            'versions:write', 'labels:manage');
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally no-op: re-inserting the retired permission rows on rollback would not
        // restore the original role_permissions grants that were merged into projects:access
        // (that mapping is lossy by design - a role could have held projects:write without
        // projects:create, and Up's dedup INSERT does not distinguish that on the way back).
        // A rollback of this migration must be a manual, reviewed DBA operation, not automatic.
    }
}
```

Confirm the generated scaffold's constructor/attributes match the existing convention in `20260729082336_AddTenantSessionExchangeChallenges.cs` (partial class, `[DbContext(typeof(ApplicationDbContext))]`/`[Migration(...)]` attributes auto-generated — leave those, only replace the `Up`/`Down` bodies as shown).

- [ ] **Step 4: Apply and verify**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Then: `psql -d <local_db> -c "SELECT code FROM permissions WHERE code LIKE 'projects:%' OR code IN ('members:read','members:manage','invitations:manage','invitations:respond','versions:write','labels:manage') ORDER BY code;"`
Expected: exactly two rows, `projects:access` and `projects:read`.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs src/ONEVO.Infrastructure/Migrations
git commit -m "feat(work-management): retire projects:write/create and unused module permissions into projects:access"
```

---

### Task 2: Schema — `Objective.ReportingManagerId` + `ObjectiveChangeRequest` entity, config, migration, RLS

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/Objective.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/ObjectiveChangeRequests/Entities/ObjectiveChangeRequest.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ObjectiveChangeRequestConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ObjectiveConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddObjectiveHierarchyAndChangeRequests.cs` (generated)

**Interfaces:**
- Produces: `Objective.ReportingManagerId`, `ObjectiveChangeRequest` entity/table, `db.ObjectiveChangeRequests` `DbSet<T>` — consumed by Task 3's repositories.

- [ ] **Step 1: Add `ReportingManagerId` to `Objective`**

Modify `src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/Objective.cs`, add one property to the existing class:

```csharp
    public Guid? ReportingManagerId { get; set; }
```

(`null` only for the Default Objective — design §3/§5. Every other Objective always has it set to its creator at creation time, Task 6.)

- [ ] **Step 2: `ObjectiveChangeRequest` entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

public static class ObjectiveChangeRequestTypes
{
    public const string Delete = "delete";
    public const string Edit = "edit";
    public const string Transfer = "transfer";
}

public static class ObjectiveChangeRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>
/// One pending/decided Delete, conflicting-Edit, or Transfer request on an Objective a non-creator
/// Head cannot apply unilaterally - see docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md.
/// </summary>
public class ObjectiveChangeRequest : BaseEntity
{
    public Guid ObjectiveId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public Guid RequestedById { get; set; }
    public Guid ReportingManagerId { get; set; }
    public string Status { get; set; } = ObjectiveChangeRequestStatuses.Pending;

    /// <summary>Proposed new field values for edit/transfer requests; null for delete.</summary>
    public string? PayloadJson { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedById { get; set; }
}
```

- [ ] **Step 3: `ObjectiveChangeRequestConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ObjectiveChangeRequestConfiguration : IEntityTypeConfiguration<ObjectiveChangeRequest>
{
    public void Configure(EntityTypeBuilder<ObjectiveChangeRequest> builder)
    {
        builder.ToTable("objective_change_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RequestType).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");

        builder.HasIndex(r => new { r.TenantId, r.ObjectiveId, r.Status })
            .HasDatabaseName("ix_objective_change_requests_tenant_id_objective_id_status");
        builder.HasIndex(r => new { r.TenantId, r.ReportingManagerId, r.Status })
            .HasDatabaseName("ix_objective_change_requests_tenant_id_reporting_manager_id_status");

        // At most one pending request per Objective (design §6) - DB-level guarantee, not just
        // handler-level, via a partial unique index on the pending rows only.
        builder.HasIndex(r => new { r.TenantId, r.ObjectiveId })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ix_objective_change_requests_one_pending_per_objective");

        builder.HasOne<Objective>()
            .WithMany()
            .HasForeignKey(r => r.ObjectiveId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 4: `ObjectiveConfiguration` — index the new column**

Add to the existing `Configure` method in `ObjectiveConfiguration.cs` (alongside the other `HasIndex` calls already there):

```csharp
        builder.HasIndex(o => new { o.TenantId, o.ReportingManagerId })
            .HasDatabaseName("ix_objectives_tenant_id_reporting_manager_id");
```

- [ ] **Step 5: Register the `DbSet`**

Add to `ApplicationDbContext.cs`, in the `// Work Management - Foundation slice` block (alongside `Objectives`, `ProjectMembers`, etc.):

```csharp
    public DbSet<ObjectiveChangeRequest> ObjectiveChangeRequests => Set<ObjectiveChangeRequest>();
```

Add `using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;` to the file's usings.

- [ ] **Step 6: Generate and extend the migration**

Run: `dotnet ef migrations add AddObjectiveHierarchyAndChangeRequests --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

Expected: creates `objective_change_requests` and adds `reporting_manager_id` to `objectives`. Add the RLS block to the bottom of `Up` and top of `Down`, following the exact pattern in Foundation's Task 3 (`docs/superpowers/plans/2026-08-03-work-management-foundation.md` Task 3 Step 2) but scoped to just the one new table:

```csharp
migrationBuilder.Sql(@"
    ALTER TABLE objective_change_requests ENABLE ROW LEVEL SECURITY;
    ALTER TABLE objective_change_requests FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS tenant_isolation ON objective_change_requests;
    CREATE POLICY tenant_isolation ON objective_change_requests
        USING (
            current_setting('app.tenant_context_mode', true) = 'admin'
            OR (
                current_setting('app.tenant_context_mode', true) = 'tenant'
                AND tenant_id::text = current_setting('app.current_tenant_id', true)
            )
        )
        WITH CHECK (
            current_setting('app.tenant_context_mode', true) = 'admin'
            OR (
                current_setting('app.tenant_context_mode', true) = 'tenant'
                AND tenant_id::text = current_setting('app.current_tenant_id', true)
            )
        );
");
```

at the end of `Up`, and

```csharp
migrationBuilder.Sql(@"
    DROP POLICY IF EXISTS tenant_isolation ON objective_change_requests;
    ALTER TABLE objective_change_requests DISABLE ROW LEVEL SECURITY;
");
```

at the top of `Down`.

- [ ] **Step 7: Apply and verify**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Then: `psql -d <local_db> -c "SELECT tablename, rowsecurity, forcerowsecurity FROM pg_tables WHERE tablename = 'objective_change_requests';"`
Expected: one row, `rowsecurity`/`forcerowsecurity` both `t`.

- [ ] **Step 8: Verify build**

Run: `dotnet build src/ONEVO.Domain/ONEVO.Domain.csproj && dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations
git commit -m "feat(work-management): add Objective.ReportingManagerId and objective_change_requests schema"
```

---

### Task 3: Repository interfaces + EF implementations

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/RepositoryInterfaces/IObjectiveChangeRequestRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveChangeRequestRepository.cs`

**Interfaces:**
- Consumes: `Objective`/`ObjectiveChangeRequest` entities (Task 2).
- Produces: every method Tasks 6-11's handlers call. `IObjectiveRepository` already has `GetDefaultByProjectIdAsync`/`Update`/`AddAsync` from Slice 2's plan and Foundation — this task adds `GetByIdForTenantAsync` and `GetTreeByProjectIdAsync`.

Same precedent as Slice 2's Task 1 (`docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md`) — plain data-access methods, no independent logic to unit-test; verified by `dotnet build` here and exercised for real by Task 13's integration tests.

- [ ] **Step 1: `IObjectiveRepository` additions**

```csharp
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;

public interface IObjectiveRepository
{
    Task AddAsync(Objective objective, CancellationToken ct = default);

    Task<Objective?> GetDefaultByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    Task<Objective?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Every Objective for a Project, unordered - the caller builds the tree from ParentObjectiveId.</summary>
    Task<IReadOnlyList<Objective>> GetTreeByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    void Update(Objective objective);
}
```

- [ ] **Step 2: `EfObjectiveRepository` additions**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfObjectiveRepository : IObjectiveRepository
{
    private readonly ApplicationDbContext _db;

    public EfObjectiveRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Objective objective, CancellationToken ct = default)
    {
        await _db.Objectives.AddAsync(objective, ct);
    }

    public async Task<Objective?> GetDefaultByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsDefault, ct);
    }

    public async Task<Objective?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == id, ct);
    }

    public async Task<IReadOnlyList<Objective>> GetTreeByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsActive)
            .ToListAsync(ct);
    }

    public void Update(Objective objective)
    {
        _db.Objectives.Update(objective);
    }
}
```

- [ ] **Step 3: `IObjectiveChangeRequestRepository`**

```csharp
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;

public interface IObjectiveChangeRequestRepository
{
    Task AddAsync(ObjectiveChangeRequest request, CancellationToken ct = default);

    Task<ObjectiveChangeRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<bool> HasPendingForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    Task<IReadOnlyList<ObjectiveChangeRequest>> ListPendingForApproverAsync(Guid tenantId, Guid reportingManagerId, CancellationToken ct = default);

    void Update(ObjectiveChangeRequest request);
}
```

- [ ] **Step 4: `EfObjectiveChangeRequestRepository`**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfObjectiveChangeRequestRepository : IObjectiveChangeRequestRepository
{
    private readonly ApplicationDbContext _db;

    public EfObjectiveChangeRequestRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ObjectiveChangeRequest request, CancellationToken ct = default)
    {
        await _db.ObjectiveChangeRequests.AddAsync(request, ct);
    }

    public async Task<ObjectiveChangeRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.ObjectiveChangeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);
    }

    public async Task<bool> HasPendingForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
    {
        return await _db.ObjectiveChangeRequests
            .AsNoTracking()
            .AnyAsync(r => r.TenantId == tenantId && r.ObjectiveId == objectiveId && r.Status == ObjectiveChangeRequestStatuses.Pending, ct);
    }

    public async Task<IReadOnlyList<ObjectiveChangeRequest>> ListPendingForApproverAsync(Guid tenantId, Guid reportingManagerId, CancellationToken ct = default)
    {
        return await _db.ObjectiveChangeRequests
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ReportingManagerId == reportingManagerId && r.Status == ObjectiveChangeRequestStatuses.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public void Update(ObjectiveChangeRequest request)
    {
        _db.ObjectiveChangeRequests.Update(request);
    }
}
```

Add `using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;` to this file's usings (for `ObjectiveChangeRequestStatuses`).

- [ ] **Step 5: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveChangeRequestRepository.cs
git commit -m "feat(work-management): add objective tree/change-request repository methods"
```

---

### Task 4: Response DTOs, ViewModels, mappers, and the shared conflict-detection helper

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveTreeItemResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs/Responses/ObjectiveChangeRequestResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Helpers/ObjectiveParentConstraintChecker.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveTreeItemViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveChangeRequestViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`

**Interfaces:**
- Produces: `ObjectiveMapper.ToDetail`/`ToTreeItem`, `ObjectiveChangeRequestMapper.ToResponse` (folded into `ObjectiveMapper.cs` — one small file, no separate mapper class needed for one DTO), `ObjectiveParentConstraintChecker.Conflicts(...)` — consumed by Tasks 5-11.

Plain data holders + one pure static rule function — no independent behavior beyond the constraint checker, which gets its own unit tests in Task 5 (it's exercised through `CreateObjectiveCommandHandlerTests`, not a standalone test file, since it's a one-method helper with no state).

- [ ] **Step 1: `ObjectiveDetailResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveDetailResponse(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
```

- [ ] **Step 2: `ObjectiveTreeItemResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveTreeItemResponse(
    Guid Id, Guid? ParentObjectiveId, bool IsDefault, string Title, Guid OwnerId,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours, bool IsActive);
```

- [ ] **Step 3: `ObjectiveChangeRequestResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;

public sealed record ObjectiveChangeRequestResponse(
    Guid Id, Guid ObjectiveId, string RequestType, Guid RequestedById, Guid ReportingManagerId,
    string Status, string? PayloadJson, DateTimeOffset? DecidedAt, Guid? DecidedById, DateTimeOffset CreatedAt);
```

- [ ] **Step 4: `ObjectiveMapper`**

```csharp
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Mappers;

public static class ObjectiveMapper
{
    public static ObjectiveDetailResponse ToDetail(Objective objective) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.CreatedAt, objective.UpdatedAt);

    public static ObjectiveTreeItemResponse ToTreeItem(Objective objective) => new(
        objective.Id, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.OwnerId,
        objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours, objective.IsActive);

    public static ObjectiveChangeRequestResponse ToResponse(ObjectiveChangeRequest request) => new(
        request.Id, request.ObjectiveId, request.RequestType, request.RequestedById, request.ReportingManagerId,
        request.Status, request.PayloadJson, request.DecidedAt, request.DecidedById, request.CreatedAt);
}
```

- [ ] **Step 5: `ObjectiveParentConstraintChecker`**

```csharp
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Helpers;

/// <summary>
/// The design's §4/§8 conflict rule: a child's date range must fall within its parent's (inclusive
/// bounds - touching the boundary is not a conflict), and the child's allocated hours must not
/// exceed the parent's total allocated hours (not remaining headroom after siblings - deliberately
/// simple, matching phase1-table-inventory.md's existing warning-only treatment of hours elsewhere).
/// Used both by Create (reject out-of-bounds children outright) and Edit (route a conflicting
/// change through approval instead of applying it).
/// </summary>
public static class ObjectiveParentConstraintChecker
{
    public static bool Conflicts(Objective parent, DateOnly startDate, DateOnly endDate, decimal allocatedHours)
    {
        var datesOutOfRange = startDate < parent.StartDate || endDate > parent.EndDate;
        var hoursExceeded = allocatedHours > parent.AllocatedHours;
        return datesOutOfRange || hoursExceeded;
    }
}
```

- [ ] **Step 6: API-layer ViewModels**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveDetailViewModel(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
```

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveTreeItemViewModel(
    Guid Id, Guid? ParentObjectiveId, bool IsDefault, string Title, Guid OwnerId,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours, bool IsActive);
```

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveChangeRequestViewModel(
    Guid Id, Guid ObjectiveId, string RequestType, Guid RequestedById, Guid ReportingManagerId,
    string Status, string? PayloadJson, DateTimeOffset? DecidedAt, Guid? DecidedById, DateTimeOffset CreatedAt);
```

- [ ] **Step 7: `ObjectiveViewModelMapper`**

```csharp
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public static class ObjectiveViewModelMapper
{
    public static ObjectiveDetailViewModel ToViewModel(this ObjectiveDetailResponse dto) => new(
        dto.Id, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.Description,
        dto.OwnerId, dto.ReportingManagerId, dto.CreatedById, dto.StartDate, dto.EndDate,
        dto.Progress, dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.CreatedAt, dto.UpdatedAt);

    public static ObjectiveTreeItemViewModel ToViewModel(this ObjectiveTreeItemResponse dto) => new(
        dto.Id, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.OwnerId,
        dto.StartDate, dto.EndDate, dto.AllocatedHours, dto.CompletedHours, dto.IsActive);

    public static ObjectiveChangeRequestViewModel ToViewModel(this ObjectiveChangeRequestResponse dto) => new(
        dto.Id, dto.ObjectiveId, dto.RequestType, dto.RequestedById, dto.ReportingManagerId,
        dto.Status, dto.PayloadJson, dto.DecidedAt, dto.DecidedById, dto.CreatedAt);
}
```

- [ ] **Step 8: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers src/ONEVO.Application/Features/WorkManagement/Objectives/Helpers src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs src/ONEVO.Api/Contracts/WorkManagement/Objectives
git commit -m "feat(work-management): add objective/change-request DTOs, view models, and the parent-constraint checker"
```

---

### Task 5: `CreateObjectiveCommand` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.GetByIdForTenantAsync`/`.AddAsync` (Task 3), `IUnitOfWork.SaveChangesAsync` (Foundation), `ObjectiveParentConstraintChecker.Conflicts` + `ObjectiveMapper.ToDetail` (Task 4).
- Produces: `CreateObjectiveCommand(Guid ParentObjectiveId, string Title, string? Description, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, Guid? HeadUserId) : IRequest<Result<ObjectiveDetailResponse>>` — consumed by Task 12's controller.

Known simplification, stated directly: `HeadUserId`, when explicitly supplied, is not validated against `IEmployeeRepository`/tenant membership — no existing Work Management handler validates an arbitrary target user this way today (`CreateProjectCommandHandler`'s `LeadId` is always the caller, never a request field), so adding that check here would be new, unrequested scope. Revisit if this becomes a real gap once the feature ships.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class CreateObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

    private static CreateObjectiveCommand ValidCommand(Guid? headUserId = null) => new(
        ParentId, "Sub Milestone", "desc", new DateOnly(2026, 2, 1), new DateOnly(2026, 5, 1), 20m, headUserId);

    private static Objective ParentObjective(Guid ownerId, bool isActive = true) => new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, Title = "Parent",
        OwnerId = ownerId, IsActive = isActive, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1),
        AllocatedHours = 40m, CreatedAt = DateTimeOffset.UtcNow
    };

    private (CreateObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(Objective? parent)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateObjectiveCommandHandler(currentUser.Object, objectives.Object, unitOfWork.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_CallerIsParentHead_CreatesWithSelfAsDefaultHeadAndReportingManager()
    {
        var (handler, objectives) = BuildHandler(ParentObjective(ownerId: UserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserId, result.Value!.OwnerId);
        Assert.Equal(UserId, result.Value.ReportingManagerId);
        objectives.Verify(x => x.AddAsync(It.Is<Objective>(o => o.OwnerId == UserId && o.ReportingManagerId == UserId && o.ParentObjectiveId == ParentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExplicitHeadUserId_ReportingManagerStaysCreatorNotTheAssignedHead()
    {
        var (handler, objectives) = BuildHandler(ParentObjective(ownerId: UserId));

        var result = await handler.Handle(ValidCommand(headUserId: OtherUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OtherUserId, result.Value!.OwnerId);
        Assert.Equal(UserId, result.Value.ReportingManagerId);
    }

    [Fact]
    public async Task Handle_CallerNotParentHead_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: OtherUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ParentNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveParent_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: UserId, isActive: false));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DatesOutsideParentRange_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: UserId));
        var command = ValidCommand() with { EndDate = new DateOnly(2026, 7, 1) };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_HoursExceedParentTotal_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(ParentObjective(ownerId: UserId));
        var command = ValidCommand() with { AllocatedHours = 999m };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter CreateObjectiveCommandHandlerTests`
Expected: FAIL to compile — types not defined.

- [ ] **Step 3: `CreateObjectiveCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public sealed record CreateObjectiveCommand(
    Guid ParentObjectiveId,
    string Title,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal AllocatedHours,
    Guid? HeadUserId
) : IRequest<Result<ObjectiveDetailResponse>>;
```

- [ ] **Step 4: `CreateObjectiveCommandValidator`**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public class CreateObjectiveCommandValidator : AbstractValidator<CreateObjectiveCommand>
{
    public CreateObjectiveCommandValidator()
    {
        RuleFor(x => x.ParentObjectiveId)
            .NotEqual(Guid.Empty).WithMessage("Parent objective is required.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(255).WithMessage("Title must be 255 characters or fewer.");

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate).WithMessage("End date must not be earlier than start date.");

        RuleFor(x => x.AllocatedHours)
            .GreaterThanOrEqualTo(0).WithMessage("Allocated hours must not be negative.");
    }
}
```

- [ ] **Step 5: `CreateObjectiveCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Helpers;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public class CreateObjectiveCommandHandler : IRequestHandler<CreateObjectiveCommand, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;

    public CreateObjectiveCommandHandler(ICurrentUser currentUser, IObjectiveRepository objectives, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(CreateObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var parent = await _objectives.GetByIdForTenantAsync(tenantId, request.ParentObjectiveId, ct);
        if (parent is null || !parent.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Parent objective not found.");

        // Free-control rule (design §4): only the parent's current Head may create a child under it.
        if (parent.OwnerId != userId)
            return Result<ObjectiveDetailResponse>.Forbidden("Only the parent milestone's head can create a sub-milestone under it.");

        if (ObjectiveParentConstraintChecker.Conflicts(parent, request.StartDate, request.EndDate, request.AllocatedHours))
            return Result<ObjectiveDetailResponse>.Failure(
                "The new milestone's date range or allocated hours would exceed the parent milestone's.");

        var now = DateTimeOffset.UtcNow;

        var objective = new Objective
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = parent.ProjectId,
            ParentObjectiveId = parent.Id,
            IsDefault = false,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            // Head defaults to the creator if not explicitly assigned (design §5).
            OwnerId = request.HeadUserId ?? userId,
            // Always the creator, regardless of who is assigned Head - a one-time fact set at
            // creation and never touched again, including by Transfer (Task 8).
            ReportingManagerId = userId,
            IsActive = true,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Progress = 0m,
            AllocatedHours = request.AllocatedHours,
            CompletedHours = 0m,
            CreatedById = userId,
            CreatedAt = now
        };

        await _objectives.AddAsync(objective, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter CreateObjectiveCommandHandlerTests`
Expected: PASS (7/7).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs
git commit -m "feat(work-management): add CreateObjectiveCommand vertical slice"
```

---

### Task 6: `EditObjectiveCommand` vertical slice — the conflict→approval branch (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveEditOutcomeResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs/EditObjectiveRequestPayload.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective/EditObjectiveCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective/EditObjectiveCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective/EditObjectiveCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EditObjectiveCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.GetByIdForTenantAsync`/`.Update` (Task 3), `IObjectiveChangeRequestRepository.HasPendingForObjectiveAsync`/`.AddAsync` (Task 3), `ObjectiveParentConstraintChecker.Conflicts` + `ObjectiveMapper.ToDetail`/`.ToResponse` (Task 4), `IUnitOfWork.SaveChangesAsync` (Foundation).
- Produces: `EditObjectiveCommand(Guid ObjectiveId, string Title, string? Description, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours) : IRequest<Result<ObjectiveEditOutcomeResponse>>` — consumed by Task 12's controller. `ObjectiveEditOutcomeResponse.Applied` tells the controller/client whether the edit took effect immediately or is now pending.

- [ ] **Step 1: `ObjectiveEditOutcomeResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveEditOutcomeResponse(
    bool Applied,
    ObjectiveDetailResponse? Objective,
    ObjectiveChangeRequests.DTOs.Responses.ObjectiveChangeRequestResponse? PendingRequest);
```

- [ ] **Step 2: `EditObjectiveRequestPayload`** — the JSON shape stored in `ObjectiveChangeRequest.PayloadJson` for a pending `edit` request

```csharp
namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;

public sealed record EditObjectiveRequestPayload(string Title, string? Description, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours);
```

- [ ] **Step 3: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class EditObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

    private static EditObjectiveCommand ValidCommand(DateOnly? endDate = null, decimal allocatedHours = 15m) => new(
        ObjectiveId, "Updated Title", "updated desc", new DateOnly(2026, 2, 1), endDate ?? new DateOnly(2026, 4, 1), allocatedHours);

    private static Objective ParentObjective() => new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, Title = "Parent",
        OwnerId = HeadId, IsActive = true, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1),
        AllocatedHours = 40m, CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective SubObjective(Guid createdById, bool isDefault = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ParentId, IsDefault = isDefault,
        Title = "Sub", OwnerId = HeadId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = true,
        StartDate = new DateOnly(2026, 1, 15), EndDate = new DateOnly(2026, 5, 1), AllocatedHours = 20m,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private (EditObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        Objective? objective, Objective? parent, bool hasPending = false, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new EditObjectiveCommandHandler(currentUser.Object, objectives.Object, requests.Object, unitOfWork.Object);
        return (handler, objectives, requests);
    }

    [Fact]
    public async Task Handle_NonConflictingEditByHead_AppliesImmediately()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: OtherUserId), ParentObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        Assert.Equal("Updated Title", result.Value.Objective!.Title);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConflictingEditByCreator_AppliesImmediately()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: HeadId), ParentObjective());
        var command = ValidCommand(endDate: new DateOnly(2026, 7, 1));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConflictingEditByNonCreatorHead_CreatesPendingRequestInsteadOfApplying()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: OtherUserId), ParentObjective());
        var command = ValidCommand(endDate: new DateOnly(2026, 7, 1));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.NotNull(result.Value.PendingRequest);
        Assert.Equal(OtherUserId, result.Value.PendingRequest!.ReportingManagerId);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConflictingEditWithAlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), ParentObjective(), hasPending: true);
        var command = ValidCommand(endDate: new DateOnly(2026, 7, 1));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), ParentObjective(), callerId: OtherUserId);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: HeadId, isDefault: true), ParentObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null, ParentObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter EditObjectiveCommandHandlerTests`
Expected: FAIL to compile — types not defined.

- [ ] **Step 5: `EditObjectiveCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;

public sealed record EditObjectiveCommand(
    Guid ObjectiveId,
    string Title,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal AllocatedHours
) : IRequest<Result<ObjectiveEditOutcomeResponse>>;
```

- [ ] **Step 6: `EditObjectiveCommandValidator`**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;

public class EditObjectiveCommandValidator : AbstractValidator<EditObjectiveCommand>
{
    public EditObjectiveCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(255).WithMessage("Title must be 255 characters or fewer.");

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate).WithMessage("End date must not be earlier than start date.");

        RuleFor(x => x.AllocatedHours)
            .GreaterThanOrEqualTo(0).WithMessage("Allocated hours must not be negative.");
    }
}
```

- [ ] **Step 7: `EditObjectiveCommandHandler`**

```csharp
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Helpers;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;

public class EditObjectiveCommandHandler : IRequestHandler<EditObjectiveCommand, Result<ObjectiveEditOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;

    public EditObjectiveCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveEditOutcomeResponse>> Handle(EditObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<ObjectiveEditOutcomeResponse>.NotFound("Objective not found.");

        // Default-Objective carve-out (design §5) - edited only via PUT /projects/{id}.
        if (objective.IsDefault)
            return Result<ObjectiveEditOutcomeResponse>.Failure("Use the Project edit endpoint for the Default Objective.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Only this milestone's head can edit it.");

        // Every non-default Objective always has a parent (Task 5 sets ParentObjectiveId at
        // creation) - loaded to run the conflict check against it.
        var parent = await _objectives.GetByIdForTenantAsync(tenantId, objective.ParentObjectiveId!.Value, ct);
        if (parent is null)
            return Result<ObjectiveEditOutcomeResponse>.NotFound("Parent objective not found.");

        var conflicts = ObjectiveParentConstraintChecker.Conflicts(parent, request.StartDate, request.EndDate, request.AllocatedHours);
        var isCreator = objective.CreatedById == userId;

        // Non-conflicting edits always apply immediately, regardless of who's asking. Conflicting
        // edits also apply immediately if the caller is the Objective's own creator - a creator
        // never needs approval for their own creation (design §4).
        if (!conflicts || isCreator)
        {
            var now = DateTimeOffset.UtcNow;
            objective.Title = request.Title.Trim();
            objective.Description = request.Description?.Trim();
            objective.StartDate = request.StartDate;
            objective.EndDate = request.EndDate;
            objective.AllocatedHours = request.AllocatedHours;
            objective.UpdatedAt = now;

            _objectives.Update(objective);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result<ObjectiveEditOutcomeResponse>.Success(
                new ObjectiveEditOutcomeResponse(Applied: true, ObjectiveMapper.ToDetail(objective), PendingRequest: null));
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveEditOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var payload = new EditObjectiveRequestPayload(request.Title.Trim(), request.Description?.Trim(), request.StartDate, request.EndDate, request.AllocatedHours);

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Edit,
            RequestedById = userId,
            // Objective.ReportingManagerId is only ever null for the Default Objective, already
            // excluded above - safe to unwrap here.
            ReportingManagerId = objective.ReportingManagerId!.Value,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _changeRequests.AddAsync(changeRequest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveEditOutcomeResponse>.Success(
            new ObjectiveEditOutcomeResponse(Applied: false, Objective: null, ObjectiveMapper.ToResponse(changeRequest)));
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter EditObjectiveCommandHandlerTests`
Expected: PASS (7/7).

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveEditOutcomeResponse.cs src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs/EditObjectiveRequestPayload.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective tests/ONEVO.Tests.Unit/Features/WorkManagement/EditObjectiveCommandHandlerTests.cs
git commit -m "feat(work-management): add EditObjectiveCommand vertical slice with conflict-to-approval branch"
```

---

### Task 7: `DeleteObjectiveCommand` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveChangeOutcomeResponse.cs` (shared by this task and Task 8 — both are "applied immediately or created a pending request" outcomes with no other payload worth returning)
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjective/DeleteObjectiveCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjective/DeleteObjectiveCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/DeleteObjectiveCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.GetByIdForTenantAsync`/`.Update`, `IObjectiveChangeRequestRepository.HasPendingForObjectiveAsync`/`.AddAsync`, `IUnitOfWork.SaveChangesAsync`.
- Produces: `DeleteObjectiveCommand(Guid ObjectiveId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>` — consumed by Task 12.

No validator — the command has no user-supplied fields beyond the route id (matches Slice 2's `DeleteProjectCommand`). No conflict check applies to Delete (design §4) — only the creator-vs-non-creator split matters. No cascade to descendants (design §4).

- [ ] **Step 1: `ObjectiveChangeOutcomeResponse`**

```csharp
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveChangeOutcomeResponse(bool Applied, ObjectiveChangeRequestResponse? PendingRequest);
```

- [ ] **Step 2: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class DeleteObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective(Guid createdById, bool isDefault = false, bool isActive = true) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, IsDefault = isDefault, Title = "Sub",
        OwnerId = HeadId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (DeleteObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        Objective? objective, bool hasPending = false, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteObjectiveCommandHandler(currentUser.Object, objectives.Object, requests.Object, unitOfWork.Object);
        return (handler, objectives, requests);
    }

    [Fact]
    public async Task Handle_CreatorHeadDeletes_AppliesImmediately()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: HeadId));

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => !o.IsActive)), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadDeletes_CreatesPendingRequest()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        Assert.Equal(OtherUserId, result.Value.PendingRequest!.ReportingManagerId);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), hasPending: true);

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), callerId: OtherUserId);

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: HeadId, isDefault: true));

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null);

        var result = await handler.Handle(new DeleteObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter DeleteObjectiveCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 4: `DeleteObjectiveCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;

public sealed record DeleteObjectiveCommand(Guid ObjectiveId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>;
```

- [ ] **Step 5: `DeleteObjectiveCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;

public class DeleteObjectiveCommandHandler : IRequestHandler<DeleteObjectiveCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteObjectiveCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(DeleteObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("Use the Project delete endpoint for the Default Objective.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can delete it.");

        if (objective.CreatedById == userId)
        {
            objective.IsActive = false;
            objective.UpdatedAt = DateTimeOffset.UtcNow;
            _objectives.Update(objective);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(Applied: true, PendingRequest: null));
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Delete,
            RequestedById = userId,
            ReportingManagerId = objective.ReportingManagerId!.Value,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = null,
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _changeRequests.AddAsync(changeRequest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveChangeOutcomeResponse>.Success(
            new ObjectiveChangeOutcomeResponse(Applied: false, ObjectiveMapper.ToResponse(changeRequest)));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter DeleteObjectiveCommandHandlerTests`
Expected: PASS (6/6).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveChangeOutcomeResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjective tests/ONEVO.Tests.Unit/Features/WorkManagement/DeleteObjectiveCommandHandlerTests.cs
git commit -m "feat(work-management): add DeleteObjectiveCommand vertical slice"
```

---

### Task 8: `TransferObjectiveHeadCommand` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs/TransferObjectiveRequestPayload.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs`

**Interfaces:**
- Consumes: same repositories as Task 7. Reuses `ObjectiveChangeOutcomeResponse` (Task 7).
- Produces: `TransferObjectiveHeadCommand(Guid ObjectiveId, Guid NewHeadUserId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>`.

Same creator-vs-non-creator split as Delete (design §4/§6 #4) — the only difference is what "applying immediately" mutates (`OwnerId`, not `IsActive`) and that a non-creator's pending request carries a payload (the proposed new Head) rather than none. Design §6's stated default: `ReportingManagerId` is never touched by Transfer, regardless of how many times headship moves.

- [ ] **Step 1: `TransferObjectiveRequestPayload`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;

public sealed record TransferObjectiveRequestPayload(Guid NewHeadUserId);
```

- [ ] **Step 2: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class TransferObjectiveHeadCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid NewHeadId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static TransferObjectiveHeadCommand ValidCommand() => new(ObjectiveId, NewHeadId);

    private static Objective SubObjective(Guid createdById, bool isDefault = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, IsDefault = isDefault, Title = "Sub",
        OwnerId = HeadId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = true,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (TransferObjectiveHeadCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        Objective? objective, bool hasPending = false, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new TransferObjectiveHeadCommandHandler(currentUser.Object, objectives.Object, requests.Object, unitOfWork.Object);
        return (handler, objectives, requests);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_AppliesImmediately()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: HeadId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.OwnerId == NewHeadId)), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadTransfers_CreatesPendingRequest()
    {
        var (handler, objectives, requests) = BuildHandler(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.Is<Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(
            r => r.RequestType == "transfer" && r.PayloadJson!.Contains(NewHeadId.ToString())), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), hasPending: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), callerId: OtherUserId);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(SubObjective(createdById: HeadId, isDefault: true));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter TransferObjectiveHeadCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 4: `TransferObjectiveHeadCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public sealed record TransferObjectiveHeadCommand(Guid ObjectiveId, Guid NewHeadUserId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>;
```

- [ ] **Step 5: `TransferObjectiveHeadCommandValidator`**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public class TransferObjectiveHeadCommandValidator : AbstractValidator<TransferObjectiveHeadCommand>
{
    public TransferObjectiveHeadCommandValidator()
    {
        RuleFor(x => x.NewHeadUserId)
            .NotEqual(Guid.Empty).WithMessage("A new head user id is required.");
    }
}
```

- [ ] **Step 6: `TransferObjectiveHeadCommandHandler`**

```csharp
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public class TransferObjectiveHeadCommandHandler : IRequestHandler<TransferObjectiveHeadCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;

    public TransferObjectiveHeadCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(TransferObjectiveHeadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("The Default Objective's head cannot be transferred.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can transfer it.");

        if (objective.CreatedById == userId)
        {
            objective.OwnerId = request.NewHeadUserId;
            objective.UpdatedAt = DateTimeOffset.UtcNow;
            _objectives.Update(objective);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(Applied: true, PendingRequest: null));
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var payload = new TransferObjectiveRequestPayload(request.NewHeadUserId);

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Transfer,
            RequestedById = userId,
            ReportingManagerId = objective.ReportingManagerId!.Value,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _changeRequests.AddAsync(changeRequest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveChangeOutcomeResponse>.Success(
            new ObjectiveChangeOutcomeResponse(Applied: false, ObjectiveMapper.ToResponse(changeRequest)));
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter TransferObjectiveHeadCommandHandlerTests`
Expected: PASS (5/5).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/DTOs/TransferObjectiveRequestPayload.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs
git commit -m "feat(work-management): add TransferObjectiveHeadCommand vertical slice"
```

---

### Task 9: Approve/Reject change-request vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RejectObjectiveChangeRequest/RejectObjectiveChangeRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RejectObjectiveChangeRequest/RejectObjectiveChangeRequestCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ApproveObjectiveChangeRequestCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/RejectObjectiveChangeRequestCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveChangeRequestRepository.GetByIdForTenantAsync`/`.Update`, `IObjectiveRepository.GetByIdForTenantAsync`/`.Update`, `IUnitOfWork.SaveChangesAsync`.
- Produces: `ApproveObjectiveChangeRequestCommand(Guid RequestId) : IRequest<Result>`, `RejectObjectiveChangeRequestCommand(Guid RequestId) : IRequest<Result>` — consumed by Task 12.

Approve applies the underlying action (soft-delete / field update / `OwnerId` reassignment) and marks the request `approved`, in one transaction (design §3: "the requester does not take a second action"). Reject only changes the request's own status (design §3: the Objective is left unchanged, the row is kept for history).

- [ ] **Step 1: Write the failing unit tests**

```csharp
using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ApproveObjectiveChangeRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid NewHeadId = Guid.NewGuid();

    private static Objective TargetObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, Title = "Sub", OwnerId = Guid.NewGuid(),
        ReportingManagerId = ManagerId, IsActive = true,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private static ObjectiveChangeRequest DeleteRequest(string status = ObjectiveChangeRequestStatuses.Pending) => new()
    {
        Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Delete,
        ReportingManagerId = ManagerId, Status = status, CreatedAt = DateTimeOffset.UtcNow
    };

    private static ObjectiveChangeRequest TransferRequest() => new()
    {
        Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Transfer,
        ReportingManagerId = ManagerId, Status = ObjectiveChangeRequestStatuses.Pending,
        PayloadJson = JsonSerializer.Serialize(new TransferObjectiveRequestPayload(NewHeadId)), CreatedAt = DateTimeOffset.UtcNow
    };

    private (ApproveObjectiveChangeRequestCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        ObjectiveChangeRequest? request, Objective? objective, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? ManagerId);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.GetByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ApproveObjectiveChangeRequestCommandHandler(currentUser.Object, requests.Object, objectives.Object, unitOfWork.Object);
        return (handler, objectives, requests);
    }

    [Fact]
    public async Task Handle_ApproveDelete_SoftDeletesObjectiveAndMarksApproved()
    {
        var (handler, objectives, requests) = BuildHandler(DeleteRequest(), TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => !o.IsActive)), Times.Once);
        requests.Verify(x => x.Update(It.Is<ObjectiveChangeRequest>(r => r.Status == ObjectiveChangeRequestStatuses.Approved)), Times.Once);
    }

    [Fact]
    public async Task Handle_ApproveTransfer_ReassignsOwnerIdFromPayload()
    {
        var (handler, objectives, _) = BuildHandler(TransferRequest(), TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.OwnerId == NewHeadId)), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotReportingManager_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(DeleteRequest(), TargetObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDecided_ReturnsConflict()
    {
        var (handler, _, _) = BuildHandler(DeleteRequest(status: ObjectiveChangeRequestStatuses.Approved), TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_RequestNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null, TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class RejectObjectiveChangeRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static ObjectiveChangeRequest PendingRequest() => new()
    {
        Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Delete,
        ReportingManagerId = ManagerId, Status = ObjectiveChangeRequestStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
    };

    private (RejectObjectiveChangeRequestCommandHandler Handler, Mock<IObjectiveChangeRequestRepository> Requests) BuildHandler(
        ObjectiveChangeRequest? request, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? ManagerId);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.GetByIdForTenantAsync(TenantId, RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RejectObjectiveChangeRequestCommandHandler(currentUser.Object, requests.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_Reject_MarksRejectedOnly()
    {
        var (handler, requests) = BuildHandler(PendingRequest());

        var result = await handler.Handle(new RejectObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        requests.Verify(x => x.Update(It.Is<ObjectiveChangeRequest>(r => r.Status == ObjectiveChangeRequestStatuses.Rejected)), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotReportingManager_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(PendingRequest(), callerId: OtherUserId);

        var result = await handler.Handle(new RejectObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_RequestNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new RejectObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "ApproveObjectiveChangeRequestCommandHandlerTests|RejectObjectiveChangeRequestCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Commands**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;

public sealed record ApproveObjectiveChangeRequestCommand(Guid RequestId) : IRequest<Result>;
```

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;

public sealed record RejectObjectiveChangeRequestCommand(Guid RequestId) : IRequest<Result>;
```

- [ ] **Step 4: `ApproveObjectiveChangeRequestCommandHandler`**

```csharp
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;

public class ApproveObjectiveChangeRequestCommandHandler : IRequestHandler<ApproveObjectiveChangeRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveObjectiveChangeRequestCommandHandler(
        ICurrentUser currentUser, IObjectiveChangeRequestRepository changeRequests,
        IObjectiveRepository objectives, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _changeRequests = changeRequests;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ApproveObjectiveChangeRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var changeRequest = await _changeRequests.GetByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (changeRequest is null)
            return Result.NotFound("Change request not found.");

        if (changeRequest.ReportingManagerId != userId)
            return Result.Forbidden("Only this request's reporting manager can approve it.");

        if (changeRequest.Status != ObjectiveChangeRequestStatuses.Pending)
            return Result.Conflict("This request has already been decided.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, changeRequest.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        var now = DateTimeOffset.UtcNow;

        switch (changeRequest.RequestType)
        {
            case ObjectiveChangeRequestTypes.Delete:
                objective.IsActive = false;
                objective.UpdatedAt = now;
                break;

            case ObjectiveChangeRequestTypes.Edit:
                var editPayload = JsonSerializer.Deserialize<EditObjectiveRequestPayload>(changeRequest.PayloadJson!)!;
                objective.Title = editPayload.Title;
                objective.Description = editPayload.Description;
                objective.StartDate = editPayload.StartDate;
                objective.EndDate = editPayload.EndDate;
                objective.AllocatedHours = editPayload.AllocatedHours;
                objective.UpdatedAt = now;
                break;

            case ObjectiveChangeRequestTypes.Transfer:
                var transferPayload = JsonSerializer.Deserialize<TransferObjectiveRequestPayload>(changeRequest.PayloadJson!)!;
                objective.OwnerId = transferPayload.NewHeadUserId;
                objective.UpdatedAt = now;
                break;
        }

        _objectives.Update(objective);

        changeRequest.Status = ObjectiveChangeRequestStatuses.Approved;
        changeRequest.DecidedAt = now;
        changeRequest.DecidedById = userId;
        _changeRequests.Update(changeRequest);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 5: `RejectObjectiveChangeRequestCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;

public class RejectObjectiveChangeRequestCommandHandler : IRequestHandler<RejectObjectiveChangeRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;

    public RejectObjectiveChangeRequestCommandHandler(
        ICurrentUser currentUser, IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RejectObjectiveChangeRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var changeRequest = await _changeRequests.GetByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (changeRequest is null)
            return Result.NotFound("Change request not found.");

        if (changeRequest.ReportingManagerId != userId)
            return Result.Forbidden("Only this request's reporting manager can reject it.");

        if (changeRequest.Status != ObjectiveChangeRequestStatuses.Pending)
            return Result.Conflict("This request has already been decided.");

        changeRequest.Status = ObjectiveChangeRequestStatuses.Rejected;
        changeRequest.DecidedAt = DateTimeOffset.UtcNow;
        changeRequest.DecidedById = userId;
        _changeRequests.Update(changeRequest);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "ApproveObjectiveChangeRequestCommandHandlerTests|RejectObjectiveChangeRequestCommandHandlerTests"`
Expected: PASS (5/5 + 3/3).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands tests/ONEVO.Tests.Unit/Features/WorkManagement/ApproveObjectiveChangeRequestCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/RejectObjectiveChangeRequestCommandHandlerTests.cs
git commit -m "feat(work-management): add Approve/Reject ObjectiveChangeRequest vertical slice"
```

---

### Task 10: `ListMyObjectiveChangeRequestsQuery` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Queries/ListMyObjectiveChangeRequests/ListMyObjectiveChangeRequestsQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Queries/ListMyObjectiveChangeRequests/ListMyObjectiveChangeRequestsQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ListMyObjectiveChangeRequestsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveChangeRequestRepository.ListPendingForApproverAsync` (Task 3).
- Produces: `ListMyObjectiveChangeRequestsQuery() : IRequest<Result<IReadOnlyList<ObjectiveChangeRequestResponse>>>` — the caller's approval queue (design §6 #7).

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ListMyObjectiveChangeRequestsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsPendingRequestsForCaller()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(ManagerId);

        var pending = new List<ObjectiveChangeRequest>
        {
            new() { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = Guid.NewGuid(), RequestType = "delete", ReportingManagerId = ManagerId, Status = "pending", CreatedAt = DateTimeOffset.UtcNow }
        };

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.ListPendingForApproverAsync(TenantId, ManagerId, It.IsAny<CancellationToken>())).ReturnsAsync(pending);

        var handler = new ListMyObjectiveChangeRequestsQueryHandler(currentUser.Object, requests.Object);

        var result = await handler.Handle(new ListMyObjectiveChangeRequestsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        var handler = new ListMyObjectiveChangeRequestsQueryHandler(currentUser.Object, requests.Object);

        var result = await handler.Handle(new ListMyObjectiveChangeRequestsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ListMyObjectiveChangeRequestsQueryHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: `ListMyObjectiveChangeRequestsQuery`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;

public sealed record ListMyObjectiveChangeRequestsQuery() : IRequest<Result<IReadOnlyList<ObjectiveChangeRequestResponse>>>;
```

- [ ] **Step 4: `ListMyObjectiveChangeRequestsQueryHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;

public class ListMyObjectiveChangeRequestsQueryHandler : IRequestHandler<ListMyObjectiveChangeRequestsQuery, Result<IReadOnlyList<ObjectiveChangeRequestResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveChangeRequestRepository _changeRequests;

    public ListMyObjectiveChangeRequestsQueryHandler(ICurrentUser currentUser, IObjectiveChangeRequestRepository changeRequests)
    {
        _currentUser = currentUser;
        _changeRequests = changeRequests;
    }

    public async Task<Result<IReadOnlyList<ObjectiveChangeRequestResponse>>> Handle(ListMyObjectiveChangeRequestsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ObjectiveChangeRequestResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ObjectiveChangeRequestResponse>>.Forbidden("Tenant context missing.");

        var pending = await _changeRequests.ListPendingForApproverAsync(tenantId, userId, ct);
        var items = pending.Select(ObjectiveMapper.ToResponse).ToList();

        return Result<IReadOnlyList<ObjectiveChangeRequestResponse>>.Success(items);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ListMyObjectiveChangeRequestsQueryHandlerTests`
Expected: PASS (2/2).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Queries tests/ONEVO.Tests.Unit/Features/WorkManagement/ListMyObjectiveChangeRequestsQueryHandlerTests.cs
git commit -m "feat(work-management): add ListMyObjectiveChangeRequestsQuery vertical slice"
```

---

### Task 11: `GetObjectiveTreeQuery` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveTreeQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectRepository.GetByIdForTenantAsync` (Slice 2's Task 1), `IProjectMemberRepository.HasActiveMembershipAsync` (Slice 2's Task 1 — reused as-is: it already checks by `ProjectId`, not per-Objective, so it directly answers "does the caller have an active membership anywhere in this project"), `IObjectiveRepository.GetTreeByProjectIdAsync` (Task 3).
- Produces: `GetObjectiveTreeQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<ObjectiveTreeItemResponse>>>` — the design §6 #8 endpoint, consumed by Task 12.

Authorization is membership-based, not `projects:read`-or-membership like Slice 2's `GetProjectByIdQueryHandler` — there is no "view any objective tree" admin permission in this design (only the two module-wide codes exist, design §2), so a caller either has a real stake in the project (an active `project_members` row somewhere in it — the Project Lead always qualifies too, since Foundation's `CreateProjectCommandHandler` always creates a creator membership row) or is denied.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveTreeQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Project ActiveProject() => new()
    {
        Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveTreeQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Project? project, bool isMember, IReadOnlyList<Objective>? tree = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(isMember);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTreeByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(tree ?? []);

        var handler = new GetObjectiveTreeQueryHandler(currentUser.Object, projects.Object, members.Object, objectives.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_ActiveMember_ReturnsTree()
    {
        var (handler, _) = BuildHandler(ActiveProject(), isMember: true);

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NotAMember_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ActiveProject(), isMember: false);

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null, isMember: true);

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetObjectiveTreeQueryHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: `GetObjectiveTreeQuery`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;

public sealed record GetObjectiveTreeQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<ObjectiveTreeItemResponse>>>;
```

- [ ] **Step 4: `GetObjectiveTreeQueryHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;

public class GetObjectiveTreeQueryHandler : IRequestHandler<GetObjectiveTreeQuery, Result<IReadOnlyList<ObjectiveTreeItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;

    public GetObjectiveTreeQueryHandler(
        ICurrentUser currentUser, IProjectRepository projects,
        IProjectMemberRepository members, IObjectiveRepository objectives)
    {
        _currentUser = currentUser;
        _projects = projects;
        _members = members;
        _objectives = objectives;
    }

    public async Task<Result<IReadOnlyList<ObjectiveTreeItemResponse>>> Handle(GetObjectiveTreeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.NotFound("Project not found.");

        var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, userId, ct);
        if (!isMember)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("You do not have access to this project's milestone tree.");

        var tree = await _objectives.GetTreeByProjectIdAsync(tenantId, project.Id, ct);
        var items = tree.Select(ObjectiveMapper.ToTreeItem).ToList();

        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Success(items);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetObjectiveTreeQueryHandlerTests`
Expected: PASS (3/3).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveTreeQueryHandlerTests.cs
git commit -m "feat(work-management): add GetObjectiveTreeQuery vertical slice"
```

---

### Task 12: `ObjectivesController` wiring

**Files:**
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/CreateObjectiveRequest.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/EditObjectiveRequest.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/TransferObjectiveHeadRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`

**Interfaces:**
- Consumes: every command/query from Tasks 5-11, `ObjectiveViewModelMapper` (Task 4).
- Produces: all 8 endpoints from design §6.

Routes 1-7 share the `api/v1/work/objectives` prefix; #8 (`GetObjectiveTree`) overrides its own route to nest under Projects (`~/api/v1/work/projects/{projectId:guid}/objectives`) rather than living on the already-large `ProjectsController` — keeping every Objective-related action in one controller. `{id:guid}` constraints throughout mean `change-requests` (a literal segment) never collides with a guid-typed route parameter at the same position, the same precedent Slice 2 already established for `mine` vs `{id:guid}`.

- [ ] **Step 1: Request contracts**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class CreateObjectiveRequest
{
    public Guid ParentObjectiveId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal AllocatedHours { get; set; }
    public Guid? HeadUserId { get; set; }
}
```

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class EditObjectiveRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal AllocatedHours { get; set; }
}
```

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class TransferObjectiveHeadRequest
{
    public Guid NewHeadUserId { get; set; }
}
```

- [ ] **Step 2: `ObjectivesController`**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Objectives;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work/objectives")]
[Authorize(Policy = "TenantPolicy")]
[RequirePermission("projects:access")]
public class ObjectivesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ObjectivesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Creates a sub-milestone under an existing Objective. Caller must be the parent's current Head.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateObjectiveRequest request, CancellationToken ct)
    {
        var command = new CreateObjectiveCommand(
            request.ParentObjectiveId, request.Title, request.Description,
            request.StartDate, request.EndDate, request.AllocatedHours, request.HeadUserId);

        var result = await _mediator.Send(command, ct);

        // No CreatedAtAction: there is no single-Objective read route in this design (design §7 -
        // only the full-tree endpoint, GetTree, exists), so there is nothing real to point a
        // Location header at. StatusCode(201, ...) returns the created resource's body without
        // fabricating a link to a route that doesn't resolve.
        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Edits a milestone. Non-conflicting edits apply immediately; edits that would conflict with the parent's date/hours constraints become a pending approval request unless the caller is the milestone's own creator.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditObjectiveRequest request, CancellationToken ct)
    {
        var command = new EditObjectiveCommand(id, request.Title, request.Description, request.StartDate, request.EndDate, request.AllocatedHours);
        var result = await _mediator.Send(command, ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? Ok(result.Value.Objective!.ToViewModel())
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Soft-deletes a milestone. Applies immediately if the caller created it; otherwise creates a pending approval request routed to the milestone's Reporting Manager.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteObjectiveCommand(id), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Reassigns a milestone's head. Same immediate-vs-pending split as Delete.</summary>
    [HttpPost("{id:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferObjectiveHeadRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransferObjectiveHeadCommand(id, request.NewHeadUserId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Approves a pending change request. Caller must be the request's Reporting Manager.</summary>
    [HttpPost("change-requests/{requestId:guid}/approve")]
    public async Task<IActionResult> ApproveChangeRequest(Guid requestId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ApproveObjectiveChangeRequestCommand(requestId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Rejects a pending change request. Caller must be the request's Reporting Manager. The Objective is left unchanged.</summary>
    [HttpPost("change-requests/{requestId:guid}/reject")]
    public async Task<IActionResult> RejectChangeRequest(Guid requestId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RejectObjectiveChangeRequestCommand(requestId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>The caller's own approval queue - pending requests where they are the Reporting Manager.</summary>
    [HttpGet("change-requests/mine")]
    public async Task<IActionResult> ListMyChangeRequests(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListMyObjectiveChangeRequestsQuery(), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(r => r.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>The full Objective tree for a Project. Caller needs an active membership somewhere in the project.</summary>
    [HttpGet("~/api/v1/work/projects/{projectId:guid}/objectives")]
    public async Task<IActionResult> GetTree(Guid projectId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveTreeQuery(projectId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(o => o.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

There is no single-Objective read endpoint in this design (design §7's explicit scope boundary — only the full-tree endpoint, `GetTree`, exists), which is why `Create` returns a plain `StatusCode(201, ...)` rather than `CreatedAtAction`: the `201`'s body carries the full created Objective directly, and no `Location` header is fabricated pointing at a route that wouldn't resolve.

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [ ] **Step 4: Run the full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: all tests pass, including every handler test from Tasks 5-11 plus every pre-existing test.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Objectives src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
git commit -m "feat(work-management): wire ObjectivesController with all 8 milestone-hierarchy endpoints"
```

---

### Task 13: Integration tests — full HTTP flow

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/Features/WorkManagement/CreateProjectEndpointTests.cs` (add new `[Fact]` methods — same fixture, same provisioned tenants, reusing Slice 2's private HTTP helpers)

**Interfaces:**
- Consumes: `_tenantA`, `_tenantB`, `_tenantACategoryId`, `SendCreateProjectAsync`, `SendJsonAsync`, `ReadJsonAsync`, `BuildGetRequest` (Slice 2's Task 8 additions).

**Scope decision, stated plainly (same reasoning as Slice 2's Task 8):** the fixture provisions exactly one authenticated user per tenant — the owner, who is both the Project Lead and (for any Objective they personally create) its Head and creator. The non-creator-Head branches (pending change-request creation, approve/reject) require a second, lower-privileged, authenticated-over-HTTP user in the same tenant — out of scope to build here, and already proven precisely at the handler-unit-test level (Tasks 6-9, mocked repositories covering every branch: non-creator-conflict → pending, already-pending → 409, wrong reporting manager → 403, already-decided → 409). HTTP coverage below sticks to what one owner-per-tenant can reach for real: Create/Edit/Delete/Transfer applying immediately (owner is always creator of what they create), the Default-Objective carve-out, cross-tenant isolation, and the tree endpoint.

- [ ] **Step 1: Add milestone-hierarchy tests**

Add inside the `CreateProjectEndpointTests` class, after the Slice 2 List tests:

```csharp
    [Fact]
    public async Task CreateObjective_ByDefaultObjectiveHead_CreatesSubMilestone()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Milestone Tree Target", "MTT1");
        var createdJson = await ReadJsonAsync(created);
        var projectId = createdJson.GetProperty("project").GetProperty("id").GetGuid();
        var defaultObjectiveId = createdJson.GetProperty("defaultObjective").GetProperty("id").GetGuid();

        var response = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Design Phase", new DateOnly(2026, 1, 15), new DateOnly(2026, 3, 1), 20m);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(response);
        json.GetProperty("parentObjectiveId").GetGuid().Should().Be(defaultObjectiveId);
        json.GetProperty("isDefault").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task CreateObjective_NestedUnderOwnSubMilestone_Succeeds()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Nested Milestone Target", "NST1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        var first = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Phase 1", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), 30m);
        var firstId = (await ReadJsonAsync(first)).GetProperty("id").GetGuid();

        var nested = await SendCreateObjectiveAsync(_tenantA, firstId, "Phase 1a", new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 1), 10m);

        nested.StatusCode.Should().Be(HttpStatusCode.Created, await nested.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateObjective_DatesOutsideParentRange_Returns400()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Conflict Target", "CFT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        // Default Objective mirrors the Project's own start/target dates (2026-01-01 to 2026-06-01
        // for a project created via SendCreateProjectAsync) - this end date is well past that.
        var response = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Out Of Range", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1), 5m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditObjective_ByCreatorHead_AppliesImmediately()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Edit Milestone Target", "EMT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Editable Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 15m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();

        var editResponse = await SendEditObjectiveAsync(_tenantA, subId, "Editable Phase Renamed", new DateOnly(2026, 1, 10), new DateOnly(2026, 3, 15), 18m);

        editResponse.StatusCode.Should().Be(HttpStatusCode.OK, await editResponse.Content.ReadAsStringAsync());
        (await ReadJsonAsync(editResponse)).GetProperty("title").GetString().Should().Be("Editable Phase Renamed");
    }

    [Fact]
    public async Task EditObjective_ConflictingButByCreator_StillAppliesImmediately()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Creator Conflict Target", "CCT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Creator Conflict Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 15m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();

        // Exceeds the Default Objective's own allocated hours (mirrors the Project's
        // defaultObjectiveAllocatedHours=40 from SendCreateProjectAsync) - a real conflict, but
        // the caller is this sub-objective's own creator, so it must still apply immediately.
        var editResponse = await SendEditObjectiveAsync(_tenantA, subId, "Creator Conflict Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 999m);

        editResponse.StatusCode.Should().Be(HttpStatusCode.OK, await editResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeleteObjective_ByCreatorHead_SoftDeletesImmediately()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Delete Milestone Target", "DMT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Deletable Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();

        var deleteResponse = await SendDeleteObjectiveAsync(_tenantA, subId);

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task EditDeleteTransfer_OnDefaultObjective_Return400()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Default Carveout Target", "DCT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        (await SendEditObjectiveAsync(_tenantA, defaultObjectiveId, "Should Not Apply", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 5m))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendDeleteObjectiveAsync(_tenantA, defaultObjectiveId))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateObjective_CrossTenantParentId_Returns404()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Cross Tenant Milestone Target", "CTM1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        var response = await SendCreateObjectiveAsync(_tenantB, defaultObjectiveId, "Should Not Apply", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), 5m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant B must not be able to see or create under tenant A's Default Objective - RLS + EF global filter scoping");
    }

    [Fact]
    public async Task GetObjectiveTree_ActiveMember_ReturnsFullTree()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Tree View Target", "TVT1");
        var createdJson = await ReadJsonAsync(created);
        var projectId = createdJson.GetProperty("project").GetProperty("id").GetGuid();
        var defaultObjectiveId = createdJson.GetProperty("defaultObjective").GetProperty("id").GetGuid();
        await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Tree Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);

        var response = await _client.SendAsync(BuildGetRequest(_tenantA, $"/api/v1/work/projects/{projectId}/objectives"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.EnumerateArray().Should().HaveCountGreaterThanOrEqualTo(2, "the Default Objective plus the one sub-milestone just created");
    }
```

- [ ] **Step 2: Add the shared HTTP helpers used above**

Add near Slice 2's `SendEditProjectAsync`/`SendDeleteProjectAsync` helpers:

```csharp
    private async Task<HttpResponseMessage> SendCreateObjectiveAsync(
        TenantSession session, Guid parentObjectiveId, string title, DateOnly startDate, DateOnly endDate, decimal allocatedHours)
    {
        var body = new { parentObjectiveId, title, description = "test description", startDate, endDate, allocatedHours, headUserId = (Guid?)null };
        return await SendJsonAsync(HttpMethod.Post, session.Host, "/api/v1/work/objectives", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    private async Task<HttpResponseMessage> SendEditObjectiveAsync(
        TenantSession session, Guid objectiveId, string title, DateOnly startDate, DateOnly endDate, decimal allocatedHours)
    {
        var body = new { title, description = "edited description", startDate, endDate, allocatedHours };
        return await SendJsonAsync(HttpMethod.Put, session.Host, $"/api/v1/work/objectives/{objectiveId}", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    private async Task<HttpResponseMessage> SendDeleteObjectiveAsync(TenantSession session, Guid objectiveId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/work/objectives/{objectiveId}");
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }
```

- [ ] **Step 3: Run the integration tests**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter CreateProjectEndpointTests`
Expected: all `[Fact]`s pass (Foundation's 3 + Slice 2's 8 + this task's 9). Requires Docker running locally (Testcontainers) and both prior slices' migrations applied.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Features/WorkManagement/CreateProjectEndpointTests.cs
git commit -m "test(work-management): add HTTP integration tests for the milestone hierarchy"
```

---

### Task 14: `docs/postman-request/` docs for the 8 new endpoints

**Files:** Create, under `docs/postman-request/Work Management/`: `Create Objective.md`, `Edit Objective.md`, `Delete Objective.md`, `Transfer Objective Head.md`, `Approve Objective Change Request.md`, `Reject Objective Change Request.md`, `List My Objective Change Requests.md`, `Get Objective Tree.md`.

Same required sections as every existing file in that folder (method+route, auth/permission line, description, request/response examples, error table, Source) per `docs/superpowers/rules/PROCESS_RULES.md` rule 6.

- [ ] **Step 1: `Create Objective.md`**

```markdown
# Create Objective

**POST** `/api/v1/work/objectives`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be the parent Objective's current Head.

## Description

Creates a sub-milestone under an existing Objective. `headUserId` (optional) assigns a different Head than the creator; omit it to default to the creator (design §5). Rejected with `400` if the new milestone's date range or allocated hours would fall outside the parent's.

## Request

```json
{ "parentObjectiveId": "guid", "title": "Design Phase", "description": "optional", "startDate": "2026-01-15", "endDate": "2026-03-01", "allocatedHours": 20, "headUserId": "guid|null" }
```

## Response

`201 Created`

```json
{ "id": "guid", "projectId": "guid", "parentObjectiveId": "guid", "isDefault": false, "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid", "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": null, "allocatedHours": 20, "completedHours": 0, "isActive": true, "createdAt": "datetime", "updatedAt": null }
```

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure, or date range/hours would exceed the parent's |
| `403` | Caller is not the parent Objective's current Head |
| `404` | Parent Objective doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Create`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
```

- [ ] **Step 2: `Edit Objective.md`**

```markdown
# Edit Objective

**PUT** `/api/v1/work/objectives/{id}`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head.

## Description

Edits a milestone. A non-conflicting edit (within the parent's date/hours bounds) applies immediately. A conflicting edit applies immediately only if the caller is the milestone's own creator; otherwise it becomes a pending request routed to the milestone's Reporting Manager. `400` if `{id}` is a Default Objective — edit it via `PUT /api/v1/work/projects/{id}` instead.

## Request

```json
{ "title": "string", "description": "optional", "startDate": "date", "endDate": "date", "allocatedHours": 18 }
```

## Response

`200 OK` (applied immediately) — the updated Objective, same shape as Create's response.
`202 Accepted` (pending) — `{ "id": "guid", "objectiveId": "guid", "requestType": "edit", "requestedById": "guid", "reportingManagerId": "guid", "status": "pending", "payloadJson": "string", "decidedAt": null, "decidedById": null, "createdAt": "datetime" }`

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure, or `{id}` is the Default Objective |
| `403` | Caller is not `{id}`'s current Head |
| `404` | Objective or its parent doesn't exist in tenant |
| `409` | A change request is already pending for this objective |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Edit`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective/EditObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
```

- [ ] **Step 3: `Delete Objective.md`**

```markdown
# Delete Objective

**DELETE** `/api/v1/work/objectives/{id}`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head.

## Description

Soft-deletes a milestone (no cascade to descendants — design §4). Applies immediately if the caller created this Objective themselves; otherwise becomes a pending request routed to the Reporting Manager. `400` if `{id}` is the Default Objective — delete it via `DELETE /api/v1/work/projects/{id}` instead.

## Response

`204 No Content` (applied immediately) or `202 Accepted` with the pending `ObjectiveChangeRequest` body (same shape as Edit's pending response, `requestType: "delete"`, `payloadJson: null`).

## Errors

| Status | Cause |
|---|---|
| `400` | `{id}` is the Default Objective |
| `403` | Caller is not `{id}`'s current Head |
| `404` | Objective doesn't exist in tenant |
| `409` | A change request is already pending for this objective |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Delete`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjective/DeleteObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
```

- [ ] **Step 4: `Transfer Objective Head.md`**

```markdown
# Transfer Objective Head

**POST** `/api/v1/work/objectives/{id}/transfer`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head.

## Description

Reassigns a milestone's Head. Same immediate-vs-pending split as Delete: applies immediately if the caller created the Objective, otherwise routes to the Reporting Manager for approval. `ReportingManagerId` is never changed by a transfer, regardless of how many times headship moves (design §6). `400` if `{id}` is the Default Objective — its head is permanently the Project Lead.

## Request

```json
{ "newHeadUserId": "guid" }
```

## Response

`204 No Content` (applied immediately) or `202 Accepted` with the pending `ObjectiveChangeRequest` body (`requestType: "transfer"`).

## Errors

| Status | Cause |
|---|---|
| `400` | Missing `newHeadUserId`, or `{id}` is the Default Objective |
| `403` | Caller is not `{id}`'s current Head |
| `404` | Objective doesn't exist in tenant |
| `409` | A change request is already pending for this objective |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Transfer`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
```

- [ ] **Step 5: `Approve Objective Change Request.md`** and **Step 6: `Reject Objective Change Request.md`**

```markdown
# Approve Objective Change Request

**POST** `/api/v1/work/objectives/change-requests/{requestId}/approve`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must equal the request's `reportingManagerId`.

## Description

Approves a pending Delete/Edit/Transfer request. Applies the underlying action (soft-delete, field update, or head reassignment) and marks the request `approved` in one transaction — no separate action by the original requester.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller is not this request's Reporting Manager |
| `404` | Request or its target Objective doesn't exist in tenant |
| `409` | Request has already been decided |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`ApproveChangeRequest`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
```

```markdown
# Reject Objective Change Request

**POST** `/api/v1/work/objectives/change-requests/{requestId}/reject`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must equal the request's `reportingManagerId`.

## Description

Rejects a pending request. The target Objective is left unchanged; the request row is kept with `status: "rejected"` for history, not deleted.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller is not this request's Reporting Manager |
| `404` | Request doesn't exist in tenant |
| `409` | Request has already been decided |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`RejectChangeRequest`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RejectObjectiveChangeRequest/RejectObjectiveChangeRequestCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
```

- [ ] **Step 7: `List My Objective Change Requests.md`**

```markdown
# List My Objective Change Requests

**GET** `/api/v1/work/objectives/change-requests/mine`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access`.

## Description

The caller's approval queue — every `pending` change request where the caller is the Reporting Manager, oldest first.

## Response

`200 OK` — a JSON array of `ObjectiveChangeRequest` objects (same shape as Edit's pending response).

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`ListMyChangeRequests`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Queries/ListMyObjectiveChangeRequests/ListMyObjectiveChangeRequestsQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
```

- [ ] **Step 8: `Get Objective Tree.md`**

```markdown
# Get Objective Tree

**GET** `/api/v1/work/projects/{projectId}/objectives`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must have an active `project_members` row somewhere in this project.

## Description

Every active Objective for a Project, flat (client builds the tree from `parentObjectiveId`). No admin/cross-user visibility permission exists for this endpoint — membership is the only access path (design §6 #8).

## Response

`200 OK` — a JSON array: `[{ "id": "guid", "parentObjectiveId": "guid|null", "isDefault": true, "title": "string", "ownerId": "guid", "startDate": "date", "endDate": "date", "allocatedHours": 40, "completedHours": 0, "isActive": true }]`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller has no active membership in this project |
| `404` | Project doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetTree`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
```

- [ ] **Step 9: Commit**

```bash
git add "docs/postman-request/Work Management/Create Objective.md" "docs/postman-request/Work Management/Edit Objective.md" "docs/postman-request/Work Management/Delete Objective.md" "docs/postman-request/Work Management/Transfer Objective Head.md" "docs/postman-request/Work Management/Approve Objective Change Request.md" "docs/postman-request/Work Management/Reject Objective Change Request.md" "docs/postman-request/Work Management/List My Objective Change Requests.md" "docs/postman-request/Work Management/Get Objective Tree.md"
git commit -m "docs(work-management): add postman-request docs for the milestone-hierarchy endpoints"
```

---

## Self-review

**Spec coverage** (against `docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md`): §2 permissions → Task 1. §3 schema → Task 2. §4 tree-authorization rule (free control over descendants, approval-on-own-node, creator exception, root exception, no-cascade) → Tasks 5-9. §5 creation + Default-Objective carve-out → Tasks 5-8, enforced in every handler that checks `IsDefault`. §6 all 8 endpoints → Tasks 5-12. §8 conflict rule → Task 4's `ObjectiveParentConstraintChecker`, consumed by Tasks 5/6.

**Placeholder scan:** no "TBD"/unshown code — every step has runnable C# or an exact command.

**Type consistency:** `ObjectiveChangeOutcomeResponse` (Task 7) is reused as-is by Task 8 (Transfer) with matching field names; `ObjectiveMapper.ToDetail`/`.ToTreeItem`/`.ToResponse` signatures (Task 4) match every call site in Tasks 5, 6, 7, 8, 10, 11; `EditObjectiveRequestPayload`/`TransferObjectiveRequestPayload` (Tasks 6, 8) are serialized in those handlers and deserialized with matching property names in Task 9's `ApproveObjectiveChangeRequestCommandHandler`; every repository method declared in Task 3 is called with matching signatures across Tasks 5-11.

