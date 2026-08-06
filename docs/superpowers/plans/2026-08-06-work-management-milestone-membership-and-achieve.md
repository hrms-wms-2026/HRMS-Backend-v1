# Work Management — Milestone Membership, Scoped Visibility, and Achieve Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close three gaps left open by the shipped milestone-hierarchy feature (Head/member assignment doesn't sync project membership; the tree endpoint isn't scoped to what a caller can actually reach; Reporting Manager is frozen at creation instead of tracking the parent's current Head) and add a new Achieve completion workflow for both Projects and Objectives, per `docs/superpowers/specs/2026-08-06-work-management-milestone-membership-and-achieve-design.md`.

**Architecture:** Same ASP.NET Core / CQRS-via-MediatR / EF Core (Npgsql/PostgreSQL) stack as every prior Work Management slice. One migration (two boolean+timestamp column pairs, no RLS changes). Two new small Application-layer services (`IMilestoneMembershipCoordinator`, `IPermissionAutoGrantService`) shared across the Create/Transfer/Achieve/member-management handlers rather than duplicating membership logic in each. `IUnitOfWork.ExecuteInTransactionAsync` (already exists) wraps every handler that touches more than one aggregate (Objective + membership rows).

**Tech Stack:** .NET / ASP.NET Core, EF Core (Npgsql), PostgreSQL, MediatR, FluentValidation, xUnit + Moq (unit), xUnit + Testcontainers (integration), `dotnet test`.

## Global Constraints

- Domain must not reference Application/Infrastructure/API/EF Core. Application must not reference Infrastructure or `HttpContext`.
- Every async method takes `CancellationToken`, is awaited; no `.Result`/`.Wait()`.
- Validation via MediatR `ValidationBehavior` (FluentValidation) only.
- `Result`/`Result<T>` exactly as `src/ONEVO.Application/Common/Models/Result.cs` defines — controllers use `result.IsSuccess ? Ok(...) : Problem(result.Error, statusCode: result.StatusCode ?? 400)`.
- `tenantId`/`userId` always resolved from `ICurrentUser` inside handlers, never trusted from the request body.
- Raw SQL is forbidden except migration RLS-policy SQL — none of this plan's tasks need RLS SQL (no new tables), so no task in this plan writes raw SQL at all.
- **Validation on every Head/member assignment** (design §3): look up `IEmployeeRepository.GetByUserIdAsync(tenantId, userId)` — null → `400`; check `EmploymentStatusId == EmploymentStatusIds.Active` (`1`) — not active → `400`. Both checks collapse to one `400` ("assigned user must be an active employee in this tenant") since the caller never needs to distinguish the two reasons.
- **Membership writes only happen when an action actually applies** — never when a change request is merely submitted (design §3). A rejected request must leave every membership row and every `ReportingManagerId` untouched.
- **`project_members` scope rule** (design §3): a row's `ObjectiveId` defines what it grants — `ObjectiveId == the Project's Default Objective` is a "direct" membership (whole-project visibility, unchanged from today); any other `ObjectiveId` is milestone-scoped (subtree-only visibility, this plan's new behavior).
- **Achieve precondition** (design §6): a node can only be Achieved once every *direct* child is already Achieved (shallow check — the tree enforces the rest transitively, bottom-up).
- **Achieved = frozen for Edit/Transfer/member-management, NOT for Delete** (design §6) — Delete keeps its existing behavior unchanged; only Edit, Transfer, and the new Add/Remove-member endpoints gain a `!objective.IsAchieved` guard, mirroring the existing `!objective.IsActive` guard exactly.
- **Reporting Manager cascade** (design §4): Transfer applying (immediate or via approval) updates `ReportingManagerId` on the transferred Objective's *direct* children only — one level, no recursion, implemented via EF Core (tracked fetch + `SaveChanges`'s automatic diffing), never a raw SQL string.
- **Known, accepted limitation** (design §7): `RequirePermissionAttribute` reads session claims (`CurrentUserService.Permissions`), not a live permission resolve — an auto-granted `projects:access` override will not take effect for that user until their next login. This plan does not attempt to fix session refresh.

---

### Task 1: Schema — `IsAchieved`/`AchievedAt` on Objective + Project, `EmploymentStatusIds`, `achieve`/`unachieve` request types

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/Objective.cs`
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Projects/Entities/Project.cs`
- Modify: `src/ONEVO.Domain/Features/WorkManagement/ObjectiveChangeRequests/Entities/ObjectiveChangeRequest.cs`
- Create: `src/ONEVO.Domain/Lookups/EmploymentStatusIds.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ObjectiveConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddObjectiveAndProjectAchievedState.cs` (generated)

**Interfaces:**
- Produces: `Objective.IsAchieved`/`AchievedAt`, `Project.IsAchieved`/`AchievedAt`, `ObjectiveChangeRequestTypes.Achieve`/`.Unachieve`, `EmploymentStatusIds.Active` — consumed by every later task in this plan.

- [ ] **Step 1: Add `IsAchieved`/`AchievedAt` to `Objective`**

Add to the existing class in `src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/Objective.cs`:

```csharp
    public bool IsAchieved { get; set; }
    public DateTimeOffset? AchievedAt { get; set; }
```

- [ ] **Step 2: Add `IsAchieved`/`AchievedAt` to `Project`**

Same two properties, added to `src/ONEVO.Domain/Features/WorkManagement/Projects/Entities/Project.cs`.

- [ ] **Step 3: Add `Achieve`/`Unachieve` request types**

In `src/ONEVO.Domain/Features/WorkManagement/ObjectiveChangeRequests/Entities/ObjectiveChangeRequest.cs`, extend the existing `ObjectiveChangeRequestTypes` static class:

```csharp
public static class ObjectiveChangeRequestTypes
{
    public const string Delete = "delete";
    public const string Edit = "edit";
    public const string Transfer = "transfer";
    public const string Achieve = "achieve";
    public const string Unachieve = "unachieve";
}
```

(Leave `ObjectiveChangeRequestStatuses` and the rest of the file untouched.)

- [ ] **Step 4: `EmploymentStatusIds`**

```csharp
namespace ONEVO.Domain.Lookups;

/// <summary>Fixed global lookup, seeded by LookupDataSeeder (Id=1 "active", Id=4 "terminated").
/// Same shape/seeding mechanism as VersionStatusIds (src/ONEVO.Domain/Features/WorkManagement/Versions/Entities/VersionStatus.cs).</summary>
public static class EmploymentStatusIds
{
    public const int Active = 1;
}
```

- [ ] **Step 5: Index the new columns**

Add to the existing `Configure` method in `ObjectiveConfiguration.cs` (alongside the other `HasIndex` calls already there):

```csharp
        builder.HasIndex(o => new { o.TenantId, o.ProjectId, o.IsAchieved })
            .HasDatabaseName("ix_objectives_tenant_id_project_id_is_achieved");
```

Add to the existing `Configure` method in `ProjectConfiguration.cs`:

```csharp
        builder.HasIndex(p => new { p.TenantId, p.IsAchieved })
            .HasDatabaseName("ix_projects_tenant_id_is_achieved");
```

- [ ] **Step 6: Generate and apply the migration**

Run: `dotnet ef migrations add AddObjectiveAndProjectAchievedState --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

Expected: adds `is_achieved boolean not null default false` and `achieved_at timestamptz null` to both `objectives` and `projects`, plus the two new indexes. No RLS block needed (no new table — both tables already have RLS from Foundation).

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

Then verify: `psql -d <local_db> -c "SELECT column_name FROM information_schema.columns WHERE table_name IN ('objectives','projects') AND column_name IN ('is_achieved','achieved_at') ORDER BY table_name, column_name;"`
Expected: 4 rows.

- [ ] **Step 7: Verify build**

Run: `dotnet build src/ONEVO.Domain/ONEVO.Domain.csproj && dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/Objective.cs src/ONEVO.Domain/Features/WorkManagement/Projects/Entities/Project.cs src/ONEVO.Domain/Features/WorkManagement/ObjectiveChangeRequests/Entities/ObjectiveChangeRequest.cs src/ONEVO.Domain/Lookups/EmploymentStatusIds.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ObjectiveConfiguration.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectConfiguration.cs src/ONEVO.Infrastructure/Migrations
git commit -m "feat(work-management): add IsAchieved/AchievedAt schema for Objective and Project"
```

---

### Task 2: `IUserPermissionOverrideRepository.AddAsync` — write path for auto-grant

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Permission/RepositoryInterfaces/IUserPermissionOverrideRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/EfAuthRepositoryPermissionOverrideTests.cs` — not needed; this is a one-line `AddAsync`, same precedent as every other `AddAsync` in this codebase (plain data-access, no independent logic). Verification is a successful build, matching Slice 2/3's own precedent for trivial repository additions.

**Interfaces:**
- Produces: `IUserPermissionOverrideRepository.AddAsync(UserPermissionOverride, CancellationToken)` — consumed by Task 4 (`PermissionAutoGrantService`).

The interface currently has only `ListForUserAsync` (read). `PermissionResolver` already reads `UserPermissionOverride` rows correctly (verified in the design doc's grounding) — this task only adds the missing write side.

- [ ] **Step 1: Add `AddAsync` to the interface**

```csharp
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;

public interface IUserPermissionOverrideRepository
{
    Task<IReadOnlyList<UserPermissionOverrideGrant>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default);

    Task AddAsync(UserPermissionOverride grant, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement in `EfAuthRepository`**

Add near the existing `ListForUserAsync` implementation in `src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs`:

```csharp
    public async Task AddAsync(UserPermissionOverride grant, CancellationToken ct = default)
    {
        await _db.UserPermissionOverrides.AddAsync(grant, ct);
    }
```

(`UserPermissionOverride` is already `using ONEVO.Domain.Features.Auth.Entities;` in this file — confirm the using is present; if not, add it.)

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Permission/RepositoryInterfaces/IUserPermissionOverrideRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs
git commit -m "feat(work-management): add IUserPermissionOverrideRepository.AddAsync write path"
```

---

### Task 3: Repository extensions — `IProjectMemberRepository` and `IObjectiveRepository`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs`

**Interfaces:**
- Produces: `IProjectMemberRepository.GetTrackedForObjectiveAsync`, `.Update`, `.HasActiveMembershipExcludingObjectiveAsync`; `IObjectiveRepository.GetTrackedActiveDirectChildrenAsync` — consumed by Task 4's `MilestoneMembershipCoordinator` and Task 8's Transfer RM-cascade.

Plain data-access methods, no independent logic to unit-test — same precedent as every other repository-only task in this feature (Slice 3 Task 3). Verified by `dotnet build` here, exercised for real by this plan's integration tests (Task 17).

- [ ] **Step 1: `IProjectMemberRepository` additions**

```csharp
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

public interface IProjectMemberRepository
{
    Task AddAsync(ProjectMember member, CancellationToken ct = default);

    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The membership row for this exact (project, objective, user) triple, regardless of
    /// IsActive — tracked, so the caller can reactivate (IsActive=true, RemovedAt=null) or
    /// deactivate (IsActive=false, RemovedAt=now) it and rely on SaveChanges's automatic partial
    /// UPDATE. Null if no row has ever existed for this triple (a genuinely new membership).
    /// </summary>
    Task<ProjectMember?> GetTrackedForObjectiveAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// True if the user has any OTHER active membership row in this project (any ObjectiveId
    /// except the one excluded) — used to decide whether removing/deactivating one milestone's
    /// membership should also drop the user from the project entirely (design §3 Transfer step 6,
    /// §6 Achieve membership cleanup).
    /// </summary>
    Task<bool> HasActiveMembershipExcludingObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, Guid excludingObjectiveId, CancellationToken ct = default);

    void Update(ProjectMember member);
}
```

- [ ] **Step 2: `EfProjectMemberRepository` additions**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectMemberRepository : IProjectMemberRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectMemberRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectMember member, CancellationToken ct = default)
    {
        await _db.ProjectMembers.AddAsync(member, ct);
    }

    public async Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId && m.IsActive, ct);
    }

    public async Task<ProjectMember?> GetTrackedForObjectiveAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.ObjectiveId == objectiveId && m.UserId == userId, ct);
    }

    public async Task<bool> HasActiveMembershipExcludingObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, Guid excludingObjectiveId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId
                        && m.ObjectiveId != excludingObjectiveId && m.IsActive, ct);
    }

    public void Update(ProjectMember member)
    {
        _db.ProjectMembers.Update(member);
    }
}
```

Note: `Update()` here is only ever called on entities fetched via `GetTrackedForObjectiveAsync` in this plan's handlers, which already tracks every column — an unconditional `Update()` call is harmless there (unlike the Project/Objective `AsNoTracking` + `Update()` bug fixed earlier in this feature), since a `ProjectMember` row has no fields that could be concurrently written by anything else. `AddAsync` stays as-is for genuinely new rows.

- [ ] **Step 3: `IObjectiveRepository` addition**

Add to the existing interface in `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`:

```csharp
    /// <summary>
    /// Every active Objective whose ParentObjectiveId is exactly this one (one level, not
    /// recursive) — tracked, for the Reporting Manager cascade on Transfer (design §4): the
    /// caller sets ReportingManagerId on each and relies on SaveChanges's automatic partial
    /// UPDATE, never calling Update() (same AsNoTracking-vs-tracked distinction as
    /// GetTrackedByIdForTenantAsync).
    /// </summary>
    Task<IReadOnlyList<Objective>> GetTrackedActiveDirectChildrenAsync(Guid tenantId, Guid parentObjectiveId, CancellationToken ct = default);
```

- [ ] **Step 4: `EfObjectiveRepository` implementation**

```csharp
    public async Task<IReadOnlyList<Objective>> GetTrackedActiveDirectChildrenAsync(Guid tenantId, Guid parentObjectiveId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .Where(o => o.TenantId == tenantId && o.ParentObjectiveId == parentObjectiveId && o.IsActive)
            .ToListAsync(ct);
    }
```

- [ ] **Step 5: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs
git commit -m "feat(work-management): add membership and direct-children repository methods"
```

---

### Task 4: `IMilestoneMembershipCoordinator` and `IPermissionAutoGrantService` (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IPermissionAutoGrantService.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/PermissionAutoGrantService.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/MilestoneMembershipCoordinatorTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/PermissionAutoGrantServiceTests.cs`

**Interfaces:**
- Consumes: `IEmployeeRepository.GetByUserIdAsync` (existing), `IProjectMemberRepository.GetTrackedForObjectiveAsync`/`.Update`/`.AddAsync`/`.HasActiveMembershipExcludingObjectiveAsync` (Task 3), `IPermissionResolver.ResolveAsync` (existing), `IPermissionRepository.GetByCodeAsync` (existing), `IUserPermissionOverrideRepository.AddAsync` (Task 2).
- Produces: `IMilestoneMembershipCoordinator.{GetActiveAssigneeAsync, UpsertMembershipAsync, DeactivateMembershipAsync, HasOtherActiveAccessAsync}`, `IPermissionAutoGrantService.EnsureGrantedAsync` — consumed by Tasks 7 (Create), 8 (Transfer), 9 (member add/remove), 11 (Achieve), 12 (Approve).

Neither service calls `SaveChangesAsync` itself — every consuming handler wraps its whole operation in one `IUnitOfWork.ExecuteInTransactionAsync` (design §3 step 7), so these services only stage changes via repository `Add`/`Update` calls.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Application.Common.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class MilestoneMembershipCoordinatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static Employee ActiveEmployee() => new() { Id = EmployeeId, TenantId = TenantId, UserId = UserId, EmploymentStatusId = EmploymentStatusIds.Active };
    private static Employee InactiveEmployee() => new() { Id = EmployeeId, TenantId = TenantId, UserId = UserId, EmploymentStatusId = 4 };

    private (MilestoneMembershipCoordinator Coordinator, Mock<IProjectMemberRepository> Members) BuildCoordinator(Employee? employee)
    {
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var members = new Mock<IProjectMemberRepository>();

        var coordinator = new MilestoneMembershipCoordinator(employees.Object, members.Object);
        return (coordinator, members);
    }

    [Fact]
    public async Task GetActiveAssigneeAsync_ActiveEmployee_ReturnsIt()
    {
        var (coordinator, _) = BuildCoordinator(ActiveEmployee());

        var result = await coordinator.GetActiveAssigneeAsync(TenantId, UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(EmployeeId, result!.Id);
    }

    [Fact]
    public async Task GetActiveAssigneeAsync_NoEmployeeRecord_ReturnsNull()
    {
        var (coordinator, _) = BuildCoordinator(null);

        var result = await coordinator.GetActiveAssigneeAsync(TenantId, UserId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveAssigneeAsync_InactiveEmployee_ReturnsNull()
    {
        var (coordinator, _) = BuildCoordinator(InactiveEmployee());

        var result = await coordinator.GetActiveAssigneeAsync(TenantId, UserId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertMembershipAsync_NoExistingRow_AddsNew()
    {
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);

        await coordinator.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, EmployeeId, CancellationToken.None);

        members.Verify(x => x.AddAsync(It.Is<ProjectMember>(m =>
            m.TenantId == TenantId && m.ProjectId == ProjectId && m.ObjectiveId == ObjectiveId &&
            m.UserId == UserId && m.EmployeeId == EmployeeId && m.IsActive &&
            m.MembershipSource == ProjectMembershipSources.ObjectiveInvitation), It.IsAny<CancellationToken>()), Times.Once);
        members.Verify(x => x.Update(It.IsAny<ProjectMember>()), Times.Never);
    }

    [Fact]
    public async Task UpsertMembershipAsync_ExistingInactiveRow_Reactivates()
    {
        var existing = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, EmployeeId = EmployeeId, IsActive = false, RemovedAt = DateTimeOffset.UtcNow };
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await coordinator.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, EmployeeId, CancellationToken.None);

        Assert.True(existing.IsActive);
        Assert.Null(existing.RemovedAt);
        members.Verify(x => x.Update(existing), Times.Once);
        members.Verify(x => x.AddAsync(It.IsAny<ProjectMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpsertMembershipAsync_ExistingActiveRow_NoOp()
    {
        var existing = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, EmployeeId = EmployeeId, IsActive = true };
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await coordinator.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, EmployeeId, CancellationToken.None);

        members.Verify(x => x.Update(It.IsAny<ProjectMember>()), Times.Never);
        members.Verify(x => x.AddAsync(It.IsAny<ProjectMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeactivateMembershipAsync_ExistingActiveRow_Deactivates()
    {
        var existing = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, EmployeeId = EmployeeId, IsActive = true };
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await coordinator.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, CancellationToken.None);

        Assert.False(existing.IsActive);
        Assert.NotNull(existing.RemovedAt);
        members.Verify(x => x.Update(existing), Times.Once);
    }

    [Fact]
    public async Task DeactivateMembershipAsync_NoExistingRow_NoOp()
    {
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.GetTrackedForObjectiveAsync(TenantId, ProjectId, ObjectiveId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectMember?)null);

        await coordinator.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, UserId, CancellationToken.None);

        members.Verify(x => x.Update(It.IsAny<ProjectMember>()), Times.Never);
    }

    [Fact]
    public async Task HasOtherActiveAccessAsync_DelegatesToRepository()
    {
        var (coordinator, members) = BuildCoordinator(ActiveEmployee());
        members.Setup(x => x.HasActiveMembershipExcludingObjectiveAsync(TenantId, ProjectId, UserId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await coordinator.HasOtherActiveAccessAsync(TenantId, ProjectId, UserId, ObjectiveId, CancellationToken.None);

        Assert.True(result);
    }
}
```

```csharp
using Moq;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.Auth.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class PermissionAutoGrantServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid GrantedByUserId = Guid.NewGuid();
    private static readonly Guid PermissionId = Guid.NewGuid();

    private (PermissionAutoGrantService Service, Mock<IUserPermissionOverrideRepository> Overrides) BuildService(
        List<string> effectivePermissions, Permission? permission)
    {
        var resolver = new Mock<IPermissionResolver>();
        resolver.Setup(x => x.ResolveAsync(UserId, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(effectivePermissions);

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(x => x.GetByCodeAsync("projects:access", It.IsAny<CancellationToken>())).ReturnsAsync(permission);

        var overrides = new Mock<IUserPermissionOverrideRepository>();

        var service = new PermissionAutoGrantService(resolver.Object, permissions.Object, overrides.Object);
        return (service, overrides);
    }

    [Fact]
    public async Task EnsureGrantedAsync_AlreadyHasPermission_DoesNothing()
    {
        var (service, overrides) = BuildService(["projects:access"], new Permission { Id = PermissionId, Code = "projects:access" });

        await service.EnsureGrantedAsync(TenantId, UserId, GrantedByUserId, "projects:access", CancellationToken.None);

        overrides.Verify(x => x.AddAsync(It.IsAny<UserPermissionOverride>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureGrantedAsync_HasWildcard_DoesNothing()
    {
        var (service, overrides) = BuildService(["*"], new Permission { Id = PermissionId, Code = "projects:access" });

        await service.EnsureGrantedAsync(TenantId, UserId, GrantedByUserId, "projects:access", CancellationToken.None);

        overrides.Verify(x => x.AddAsync(It.IsAny<UserPermissionOverride>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureGrantedAsync_MissingPermission_AddsGrantOverride()
    {
        var (service, overrides) = BuildService([], new Permission { Id = PermissionId, Code = "projects:access" });

        await service.EnsureGrantedAsync(TenantId, UserId, GrantedByUserId, "projects:access", CancellationToken.None);

        overrides.Verify(x => x.AddAsync(It.Is<UserPermissionOverride>(o =>
            o.TenantId == TenantId && o.UserId == UserId && o.PermissionId == PermissionId &&
            o.GrantType == "grant" && o.GrantedBy == GrantedByUserId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureGrantedAsync_PermissionCodeNotSeeded_DoesNothingDefensively()
    {
        var (service, overrides) = BuildService([], null);

        await service.EnsureGrantedAsync(TenantId, UserId, GrantedByUserId, "projects:access", CancellationToken.None);

        overrides.Verify(x => x.AddAsync(It.IsAny<UserPermissionOverride>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~MilestoneMembershipCoordinatorTests|FullyQualifiedName~PermissionAutoGrantServiceTests"`
Expected: FAIL to compile — types not defined.

- [ ] **Step 3: `IMilestoneMembershipCoordinator`**

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

/// <summary>
/// Encapsulates the membership-lifecycle rules from
/// docs/superpowers/specs/2026-08-06-work-management-milestone-membership-and-achieve-design.md
/// §3, shared across Create/Transfer/Achieve/member-management. Never calls SaveChangesAsync -
/// callers wrap the whole operation in IUnitOfWork.ExecuteInTransactionAsync.
/// </summary>
public interface IMilestoneMembershipCoordinator
{
    /// <summary>Null if the user has no Employee record in this tenant, or their EmploymentStatusId isn't Active.</summary>
    Task<Employee?> GetActiveAssigneeAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Creates a new milestone-scoped membership, or reactivates an existing inactive one. No-op if already active.</summary>
    Task UpsertMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Deactivates the membership for this exact (project, objective, user) triple. No-op if no row exists.</summary>
    Task DeactivateMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default);

    /// <summary>True if the user has any other active membership in this project (direct or a different milestone).</summary>
    Task<bool> HasOtherActiveAccessAsync(Guid tenantId, Guid projectId, Guid userId, Guid excludingObjectiveId, CancellationToken ct = default);
}
```

- [ ] **Step 4: `MilestoneMembershipCoordinator`**

```csharp
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public class MilestoneMembershipCoordinator : IMilestoneMembershipCoordinator
{
    private readonly IEmployeeRepository _employees;
    private readonly IProjectMemberRepository _members;

    public MilestoneMembershipCoordinator(IEmployeeRepository employees, IProjectMemberRepository members)
    {
        _employees = employees;
        _members = members;
    }

    public async Task<Employee?> GetActiveAssigneeAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByUserIdAsync(tenantId, userId, ct);
        return employee is not null && employee.EmploymentStatusId == EmploymentStatusIds.Active ? employee : null;
    }

    public async Task UpsertMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, Guid employeeId, CancellationToken ct = default)
    {
        var existing = await _members.GetTrackedForObjectiveAsync(tenantId, projectId, objectiveId, userId, ct);

        if (existing is null)
        {
            await _members.AddAsync(new ProjectMember
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = projectId,
                ObjectiveId = objectiveId,
                UserId = userId,
                EmployeeId = employeeId,
                MembershipSource = ProjectMembershipSources.ObjectiveInvitation,
                IsActive = true,
                JoinedAt = DateTimeOffset.UtcNow,
                CreatedById = userId,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
            return;
        }

        if (existing.IsActive)
            return;

        existing.IsActive = true;
        existing.RemovedAt = null;
        existing.JoinedAt = DateTimeOffset.UtcNow;
        _members.Update(existing);
    }

    public async Task DeactivateMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid userId, CancellationToken ct = default)
    {
        var existing = await _members.GetTrackedForObjectiveAsync(tenantId, projectId, objectiveId, userId, ct);
        if (existing is null || !existing.IsActive)
            return;

        existing.IsActive = false;
        existing.RemovedAt = DateTimeOffset.UtcNow;
        _members.Update(existing);
    }

    public Task<bool> HasOtherActiveAccessAsync(Guid tenantId, Guid projectId, Guid userId, Guid excludingObjectiveId, CancellationToken ct = default)
        => _members.HasActiveMembershipExcludingObjectiveAsync(tenantId, projectId, userId, excludingObjectiveId, ct);
}
```

- [ ] **Step 5: `IPermissionAutoGrantService`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

/// <summary>
/// Auto-grants a permission code to a user via a UserPermissionOverride row if their currently
/// effective permission set doesn't already include it - design §7. Known limitation: the grant
/// takes effect only on the user's next login, since RequirePermissionAttribute reads session
/// claims, not a live IPermissionResolver.ResolveAsync call.
/// </summary>
public interface IPermissionAutoGrantService
{
    Task EnsureGrantedAsync(Guid tenantId, Guid userId, Guid grantedByUserId, string permissionCode, CancellationToken ct = default);
}
```

- [ ] **Step 6: `PermissionAutoGrantService`**

```csharp
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public class PermissionAutoGrantService : IPermissionAutoGrantService
{
    private readonly IPermissionResolver _permissionResolver;
    private readonly IPermissionRepository _permissions;
    private readonly IUserPermissionOverrideRepository _overrides;

    public PermissionAutoGrantService(
        IPermissionResolver permissionResolver, IPermissionRepository permissions, IUserPermissionOverrideRepository overrides)
    {
        _permissionResolver = permissionResolver;
        _permissions = permissions;
        _overrides = overrides;
    }

    public async Task EnsureGrantedAsync(Guid tenantId, Guid userId, Guid grantedByUserId, string permissionCode, CancellationToken ct = default)
    {
        var effective = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
        if (effective.Contains(permissionCode) || effective.Contains("*"))
            return;

        var permission = await _permissions.GetByCodeAsync(permissionCode, ct);
        if (permission is null)
            return;

        await _overrides.AddAsync(new UserPermissionOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            PermissionId = permission.Id,
            GrantType = "grant",
            Reason = "Auto-granted on milestone head assignment",
            GrantedBy = grantedByUserId,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);
    }
}
```

- [ ] **Step 7: Register both services in DI**

Add to `src/ONEVO.Infrastructure/DependencyInjection.cs`, alongside the other Work Management registrations:

```csharp
        services.AddScoped<IMilestoneMembershipCoordinator, MilestoneMembershipCoordinator>();
        services.AddScoped<IPermissionAutoGrantService, PermissionAutoGrantService>();
```

Add `using ONEVO.Application.Features.WorkManagement.Objectives.Services;` to the file's usings.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~MilestoneMembershipCoordinatorTests|FullyQualifiedName~PermissionAutoGrantServiceTests"`
Expected: PASS (12/12 — 8 coordinator + 4 auto-grant).

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Services src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/MilestoneMembershipCoordinatorTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/PermissionAutoGrantServiceTests.cs
git commit -m "feat(work-management): add MilestoneMembershipCoordinator and PermissionAutoGrantService"
```

---

### Task 5: DTOs/ViewModels — `IsAchieved`/`AchievedAt` exposure + member request/response types

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveTreeItemResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectDetailResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveTreeItemViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectDetailViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectViewModelMapper.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/AddObjectiveMemberRequest.cs`

**Interfaces:**
- Produces: `ObjectiveDetailResponse`/`ObjectiveTreeItemResponse`/`ProjectDetailResponse` all gain `IsAchieved`/`AchievedAt` — consumed by every handler/test in this plan that maps an Objective or Project.

Plain data holders and pure mapping functions — no independent behavior. Verification is a successful build, same precedent as every DTO-only task in this feature.

- [ ] **Step 1: `ObjectiveDetailResponse`**

Add two fields to the existing record:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveDetailResponse(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
```

- [ ] **Step 2: `ObjectiveTreeItemResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveTreeItemResponse(
    Guid Id, Guid? ParentObjectiveId, bool IsDefault, string Title, Guid OwnerId,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours, bool IsActive, bool IsAchieved);
```

- [ ] **Step 3: Update `ObjectiveMapper.ToDetail`/`ToTreeItem`**

```csharp
    public static ObjectiveDetailResponse ToDetail(Objective objective) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.IsAchieved, objective.AchievedAt, objective.CreatedAt, objective.UpdatedAt);

    public static ObjectiveTreeItemResponse ToTreeItem(Objective objective) => new(
        objective.Id, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.OwnerId,
        objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours, objective.IsActive, objective.IsAchieved);
```

(Leave `ToResponse(ObjectiveChangeRequest)` in the same file untouched.)

- [ ] **Step 4: `ProjectDetailResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

public sealed record ProjectDetailResponse(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, bool IsLead);
```

- [ ] **Step 5: Update `ProjectMapper.ToDetail`**

```csharp
    public static ProjectDetailResponse ToDetail(Project project, bool isLead) => new(
        project.Id, project.Name, project.Identifier, project.CategoryId, project.Description,
        project.LeadId, project.StartDate, project.TargetDate, project.Color,
        project.ActualHours, project.AllocatedHours, project.CompletedHours,
        project.IsActive, project.IsAchieved, project.AchievedAt,
        project.CreatedAt, project.UpdatedAt, isLead);
```

(Leave `ToListItem`/`ToSummary` untouched — `IsAchieved` isn't needed in list rows for this plan's scope; `ProjectListItemResponse` stays as-is.)

- [ ] **Step 6: API-layer ViewModels — mirror the two response changes**

`ObjectiveDetailViewModel.cs`:

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveDetailViewModel(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
```

`ObjectiveTreeItemViewModel.cs`:

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveTreeItemViewModel(
    Guid Id, Guid? ParentObjectiveId, bool IsDefault, string Title, Guid OwnerId,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours, bool IsActive, bool IsAchieved);
```

`ProjectDetailViewModel.cs`:

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public sealed record ProjectDetailViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, bool IsLead);
```

- [ ] **Step 7: Update `ObjectiveViewModelMapper`/`ProjectViewModelMapper`**

In `ObjectiveViewModelMapper.cs`:

```csharp
    public static ObjectiveDetailViewModel ToViewModel(this ObjectiveDetailResponse dto) => new(
        dto.Id, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.Description,
        dto.OwnerId, dto.ReportingManagerId, dto.CreatedById, dto.StartDate, dto.EndDate,
        dto.Progress, dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.IsAchieved, dto.AchievedAt, dto.CreatedAt, dto.UpdatedAt);

    public static ObjectiveTreeItemViewModel ToViewModel(this ObjectiveTreeItemResponse dto) => new(
        dto.Id, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.OwnerId,
        dto.StartDate, dto.EndDate, dto.AllocatedHours, dto.CompletedHours, dto.IsActive, dto.IsAchieved);
```

In `ProjectViewModelMapper.cs`:

```csharp
    public static ProjectDetailViewModel ToViewModel(this ProjectDetailResponse dto) => new(
        dto.Id, dto.Name, dto.Identifier, dto.CategoryId, dto.Description,
        dto.LeadId, dto.StartDate, dto.TargetDate, dto.Color,
        dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.IsAchieved, dto.AchievedAt,
        dto.CreatedAt, dto.UpdatedAt, dto.IsLead);
```

(Both files' other mapping methods — `ToViewModel(ObjectiveChangeRequestResponse)`, `ToViewModel(ProjectListItemResponse)`, `ToViewModel(PagedResult<...>)` — stay untouched.)

- [ ] **Step 8: `AddObjectiveMemberRequest`** (new — consumed by Task 8)

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class AddObjectiveMemberRequest
{
    public Guid UserId { get; set; }
}
```

- [ ] **Step 9: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors. (This will surface every existing call site that constructs one of the changed records positionally — fix each to pass the two new arguments; there are no such call sites outside the mappers themselves and the test files touched by later tasks, since `ObjectiveMapper.ToDetail`/`ToTreeItem` and `ProjectMapper.ToDetail` are the only production constructors, already updated above.)

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveTreeItemResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectDetailResponse.cs src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveTreeItemViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectDetailViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectViewModelMapper.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/AddObjectiveMemberRequest.cs
git commit -m "feat(work-management): expose IsAchieved/AchievedAt on Objective/Project DTOs and view models"
```

---

### Task 6: `CreateObjectiveCommand` — membership sync, employee validation, auto-grant (unit-tested)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs` (extend the existing file)

**Interfaces:**
- Consumes: `IMilestoneMembershipCoordinator.{GetActiveAssigneeAsync, UpsertMembershipAsync}` (Task 4), `IPermissionAutoGrantService.EnsureGrantedAsync` (Task 4), `IUnitOfWork.ExecuteInTransactionAsync` (existing).
- Produces: same `CreateObjectiveCommand` signature as before — no new request fields (member list stays out of scope, matching the design's confirmed "Head only for now" resolution — a separate member-add call, Task 8, handles it).

Existing tests (`Handle_CallerIsParentHead_...`, `Handle_ExplicitHeadUserId_...`, `Handle_CallerNotParentHead_...`, `Handle_ParentNotFound_...`, `Handle_InactiveParent_...`, `Handle_DatesOutsideParentRange_...`, `Handle_HoursExceedParentTotal_...`) must all keep passing unmodified — this task only adds new behavior and new tests for it.

- [ ] **Step 1: Add the new failing unit tests to the existing test file**

Add to `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs`, alongside the existing tests (extend `BuildHandler` to also construct/inject the two new dependencies, defaulting to "assignee is a valid active employee" and "no auto-grant needed" so the seven existing tests keep passing without modification):

```csharp
    private (CreateObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IMilestoneMembershipCoordinator> Membership, Mock<IPermissionAutoGrantService> AutoGrant) BuildHandlerWithMembership(
        Objective? parent, Employee? assignee = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignee ?? new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, EmploymentStatusId = EmploymentStatusIds.Active });

        var autoGrant = new Mock<IPermissionAutoGrantService>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveDetailResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateObjectiveCommandHandler(currentUser.Object, objectives.Object, unitOfWork.Object, membership.Object, autoGrant.Object);
        return (handler, objectives, membership, autoGrant);
    }

    [Fact]
    public async Task Handle_ValidCreate_UpsertsMembershipForResolvedHead()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(ParentObjective(ownerId: UserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, It.IsAny<Guid>(), UserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExplicitHeadUserId_UpsertsMembershipForThatHeadNotCaller()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(ParentObjective(ownerId: UserId));

        var result = await handler.Handle(ValidCommand(headUserId: OtherUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, It.IsAny<Guid>(), OtherUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AssignedHeadNotActiveEmployee_ReturnsBadRequest()
    {
        var (handler, _, _, _) = BuildHandlerWithMembership(ParentObjective(ownerId: UserId), assignee: null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ValidCreate_EnsuresProjectsAccessGrantedForResolvedHead()
    {
        var (handler, _, _, autoGrant) = BuildHandlerWithMembership(ParentObjective(ownerId: UserId));

        await handler.Handle(ValidCommand(), CancellationToken.None);

        autoGrant.Verify(x => x.EnsureGrantedAsync(TenantId, UserId, UserId, "projects:access", It.IsAny<CancellationToken>()), Times.Once);
    }
```

Add the necessary `using`s (`ONEVO.Application.Features.WorkManagement.Objectives.Services`, `ONEVO.Domain.Features.CoreHr.Entities`, `ONEVO.Domain.Lookups`) to the top of the test file.

- [ ] **Step 2: Run tests to verify the new ones fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter CreateObjectiveCommandHandlerTests`
Expected: FAIL to compile — the handler's constructor doesn't accept the two new dependencies yet.

- [ ] **Step 3: Update `CreateObjectiveCommandHandler`**

Replace the handler's constructor and `Handle` method:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Helpers;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public class CreateObjectiveCommandHandler : IRequestHandler<CreateObjectiveCommand, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;

    public CreateObjectiveCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
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

        var resolvedHeadUserId = request.HeadUserId ?? userId;
        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, resolvedHeadUserId, ct);
        if (assignee is null)
            return Result<ObjectiveDetailResponse>.Failure("The assigned head must be an active employee in this tenant.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
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
                OwnerId = resolvedHeadUserId,
                // Always the creator, regardless of who is assigned Head - a one-time fact set at
                // creation, later kept in sync with the PARENT's current head by Transfer's
                // cascade (design §4), not by anything in this handler.
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

            await _objectives.AddAsync(objective, innerCt);

            // Membership sync + auto-grant (design §3/§7) - happens for every Create, whether the
            // Head is the caller (default) or an explicitly assigned headUserId.
            await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, resolvedHeadUserId, assignee.Id, innerCt);
            await _autoGrant.EnsureGrantedAsync(tenantId, resolvedHeadUserId, userId, "projects:access", innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
        }, ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter CreateObjectiveCommandHandlerTests`
Expected: PASS (11/11 — the original 7 plus 4 new).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateObjectiveCommandHandlerTests.cs
git commit -m "feat(work-management): sync membership and auto-grant projects:access on CreateObjective"
```

---

### Task 7: `TransferObjectiveHeadCommand` — membership sync, Reporting Manager cascade, auto-grant, transactional (unit-tested)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs` (extend the existing file)

**Interfaces:**
- Consumes: `IMilestoneMembershipCoordinator.{GetActiveAssigneeAsync, UpsertMembershipAsync, DeactivateMembershipAsync, HasOtherActiveAccessAsync}`, `IPermissionAutoGrantService.EnsureGrantedAsync`, `IObjectiveRepository.GetTrackedActiveDirectChildrenAsync` (Task 3), `IUnitOfWork.ExecuteInTransactionAsync`.
- Produces: same `TransferObjectiveHeadCommand` signature — no request changes.

All of §3 steps 1–6 (validate new Head, reassign `OwnerId`, cascade `ReportingManagerId` to direct children, upsert new Head's membership, deactivate old Head's membership on this milestone, drop old Head from the project entirely if they have no other access) only run on the **immediate-apply** branch (`objective.CreatedById == userId`) — the pending-request branch is untouched by this task, since none of these side effects may happen until the request is later approved (Task 11 wires the identical logic into `ApproveObjectiveChangeRequestCommandHandler`).

- [ ] **Step 1: Add the new failing unit tests**

Add to `tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs`:

```csharp
    private (TransferObjectiveHeadCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IMilestoneMembershipCoordinator> Membership, Mock<IPermissionAutoGrantService> AutoGrant) BuildHandlerWithMembership(
        Objective? objective, Employee? newHeadAssignee = null, bool oldHeadHasOtherAccess = false)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, NewHeadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newHeadAssignee ?? new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = NewHeadId, EmploymentStatusId = EmploymentStatusIds.Active });
        membership.Setup(x => x.HasOtherActiveAccessAsync(TenantId, ProjectId, HeadId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldHeadHasOtherAccess);

        var autoGrant = new Mock<IPermissionAutoGrantService>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new TransferObjectiveHeadCommandHandler(
            currentUser.Object, objectives.Object, requests.Object, unitOfWork.Object, membership.Object, autoGrant.Object);
        return (handler, objectives, membership, autoGrant);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_UpsertsNewHeadMembershipAndDeactivatesOld()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadId), oldHeadHasOtherAccess: false);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, NewHeadId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, HeadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OldHeadHasNoOtherAccess_DropsThemFromProject()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadId), oldHeadHasOtherAccess: false);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        membership.Verify(x => x.HasOtherActiveAccessAsync(TenantId, ProjectId, HeadId, ObjectiveId, It.IsAny<CancellationToken>()), Times.Once);
        // DeactivateMembershipAsync on THIS objective already ran regardless (verified above) -
        // HasOtherActiveAccessAsync being checked at all is what "drop from project" reduces to,
        // since deactivating the one membership row IS the full removal when there's no other row.
    }

    [Fact]
    public async Task Handle_NewHeadNotActiveEmployee_ReturnsBadRequest()
    {
        var (handler, _, _, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadId), newHeadAssignee: null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_CascadesReportingManagerToDirectChildren()
    {
        var child = SubObjective(createdById: OtherUserId);
        child.Id = Guid.NewGuid();
        child.ParentObjectiveId = ObjectiveId;
        child.ReportingManagerId = HeadId;

        var (handler, objectives, _, _) = BuildHandlerWithMembership(SubObjective(createdById: HeadId));
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { child });

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewHeadId, child.ReportingManagerId);
    }

    [Fact]
    public async Task Handle_CreatorHeadTransfers_EnsuresProjectsAccessGrantedForNewHead()
    {
        var (handler, _, _, autoGrant) = BuildHandlerWithMembership(SubObjective(createdById: HeadId));

        await handler.Handle(ValidCommand(), CancellationToken.None);

        autoGrant.Verify(x => x.EnsureGrantedAsync(TenantId, NewHeadId, HeadId, "projects:access", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadTransfers_DoesNotTouchMembershipYet()
    {
        var (handler, _, membership, _) = BuildHandlerWithMembership(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        membership.Verify(x => x.UpsertMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        membership.Verify(x => x.DeactivateMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

`SubObjective(...)` in this test file must set `ProjectId = ProjectId` (a test-fixture constant) if it doesn't already — check the existing factory and add it if missing, since these new tests assert on `ProjectId` being passed through to the membership calls. Add `private static readonly Guid NewHeadId = Guid.NewGuid();` alongside the file's other `Guid` constants if not already present (Task 8 of the original milestone-hierarchy plan already defined this one — reuse it, don't redeclare).

- [ ] **Step 2: Run tests to verify the new ones fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter TransferObjectiveHeadCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Update `TransferObjectiveHeadCommandHandler`**

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
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public class TransferObjectiveHeadCommandHandler : IRequestHandler<TransferObjectiveHeadCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;

    public TransferObjectiveHeadCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
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
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("The Default Objective's head cannot be transferred.");

        if (objective.IsAchieved)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("An achieved milestone's head cannot be transferred.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can transfer it.");

        if (objective.CreatedById == userId)
        {
            var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, request.NewHeadUserId, ct);
            if (newHeadAssignee is null)
                return Result<ObjectiveChangeOutcomeResponse>.Failure("The new head must be an active employee in this tenant.");

            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                var oldHeadUserId = objective.OwnerId;

                objective.OwnerId = request.NewHeadUserId;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                // Reporting Manager cascade (design §4): direct children only, one level.
                var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                foreach (var child in directChildren)
                {
                    child.ReportingManagerId = request.NewHeadUserId;
                    child.UpdatedAt = now;
                }

                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.NewHeadUserId, newHeadAssignee.Id, innerCt);
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadUserId, innerCt);
                await _autoGrant.EnsureGrantedAsync(tenantId, request.NewHeadUserId, userId, "projects:access", innerCt);

                // Old head keeps whatever other access they have (another milestone, or a direct
                // membership); if none, DeactivateMembershipAsync above already removed their only
                // row, so there's nothing further to do here beyond the check itself (design §3
                // step 6 - the "drop from project entirely" case has no separate action once the
                // one row they had is gone).
                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadUserId, objective.Id, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);

                return Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(Applied: true, PendingRequest: null));
            }, ct);
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

Note the new `!objective.IsAchieved` guard added alongside the Default-Objective check — this is this task's slice of the freeze rule (design §6); Edit's and the member-management endpoints' own freeze guards are added in Tasks 8 and 11.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter TransferObjectiveHeadCommandHandlerTests`
Expected: PASS (all original tests + 6 new).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/TransferObjectiveHeadCommandHandlerTests.cs
git commit -m "feat(work-management): sync membership, cascade ReportingManagerId, and auto-grant on TransferObjectiveHead"
```

---

### Task 8: `AddObjectiveMemberCommand` + `RemoveObjectiveMemberCommand` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/RemoveObjectiveMemberCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.GetByIdForTenantAsync`, `IMilestoneMembershipCoordinator.{GetActiveAssigneeAsync, UpsertMembershipAsync, DeactivateMembershipAsync}`, `IUnitOfWork.SaveChangesAsync`.
- Produces: `AddObjectiveMemberCommand(Guid ObjectiveId, Guid UserId) : IRequest<Result>`, `RemoveObjectiveMemberCommand(Guid ObjectiveId, Guid UserId) : IRequest<Result>` — consumed by Task 15's controller.

**Design confirmation, stated plainly:** member add/remove is Head-only, no approval-routing (unlike Delete/Edit-conflict/Transfer) — "milestone creation permission" was confirmed to mean "is the current Head," and member management is the same authorization, not a separate delegable grant. Adding a member does NOT auto-grant `projects:access` — only assigning someone as Head does (Tasks 6/7); a plain member needs no special permission to be added. Removing the current Head via this endpoint is rejected — that's what Transfer is for, and allowing it here would silently break the "Head always has a membership row" invariant this plan establishes.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AddObjectiveMemberCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid MemberUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective(bool isActive = true, bool isAchieved = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = false, Title = "Sub",
        OwnerId = HeadId, IsActive = isActive, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (AddObjectiveMemberCommandHandler Handler, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandler(
        Objective? objective, Employee? assignee = null, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, MemberUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignee ?? new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = MemberUserId, EmploymentStatusId = EmploymentStatusIds.Active });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AddObjectiveMemberCommandHandler(currentUser.Object, objectives.Object, membership.Object, unitOfWork.Object);
        return (handler, membership);
    }

    [Fact]
    public async Task Handle_HeadAddsMember_UpsertsMembership()
    {
        var (handler, membership) = BuildHandler(SubObjective());

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, MemberUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(SubObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MemberNotActiveEmployee_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(SubObjective(), assignee: null);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(SubObjective(isAchieved: true));

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new AddObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

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
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class RemoveObjectiveMemberCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid MemberUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective(bool isAchieved = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = false, Title = "Sub",
        OwnerId = HeadId, IsActive = true, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (RemoveObjectiveMemberCommandHandler Handler, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandler(
        Objective? objective, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new RemoveObjectiveMemberCommandHandler(currentUser.Object, objectives.Object, membership.Object, unitOfWork.Object);
        return (handler, membership);
    }

    [Fact]
    public async Task Handle_HeadRemovesMember_DeactivatesMembership()
    {
        var (handler, membership) = BuildHandler(SubObjective());

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, MemberUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TargetIsCurrentHead_ReturnsBadRequest()
    {
        var (handler, membership) = BuildHandler(SubObjective());

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, HeadId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        membership.Verify(x => x.DeactivateMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(SubObjective(), callerId: OtherUserId);

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var (handler, _) = BuildHandler(SubObjective(isAchieved: true));

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new RemoveObjectiveMemberCommand(ObjectiveId, MemberUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AddObjectiveMemberCommandHandlerTests|FullyQualifiedName~RemoveObjectiveMemberCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: `AddObjectiveMemberCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

public sealed record AddObjectiveMemberCommand(Guid ObjectiveId, Guid UserId) : IRequest<Result>;
```

- [ ] **Step 4: `AddObjectiveMemberCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

public class AddObjectiveMemberCommandHandler : IRequestHandler<AddObjectiveMemberCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public AddObjectiveMemberCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AddObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result.Failure("Cannot add members to an achieved milestone.");

        if (objective.OwnerId != userId)
            return Result.Forbidden("Only this milestone's head can add members.");

        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, request.UserId, ct);
        if (assignee is null)
            return Result.Failure("The member must be an active employee in this tenant.");

        await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.UserId, assignee.Id, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 5: `RemoveObjectiveMemberCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;

public sealed record RemoveObjectiveMemberCommand(Guid ObjectiveId, Guid UserId) : IRequest<Result>;
```

- [ ] **Step 6: `RemoveObjectiveMemberCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;

public class RemoveObjectiveMemberCommandHandler : IRequestHandler<RemoveObjectiveMemberCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveObjectiveMemberCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result.Failure("Cannot remove members from an achieved milestone.");

        if (objective.OwnerId != userId)
            return Result.Forbidden("Only this milestone's head can remove members.");

        // The Head is always a member too (design §3) - removing them here would break that
        // invariant. Transfer is the only supported way to move headship off this milestone.
        if (request.UserId == objective.OwnerId)
            return Result.Failure("Cannot remove the milestone's head as a member - use Transfer instead.");

        await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, request.UserId, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AddObjectiveMemberCommandHandlerTests|FullyQualifiedName~RemoveObjectiveMemberCommandHandlerTests"`
Expected: PASS (5/5 + 5/5).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember tests/ONEVO.Tests.Unit/Features/WorkManagement/AddObjectiveMemberCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/RemoveObjectiveMemberCommandHandlerTests.cs
git commit -m "feat(work-management): add Add/RemoveObjectiveMember vertical slices"
```

---

### Task 9: `AchieveObjectiveCommand` + `UnachieveObjectiveCommand` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective/AchieveObjectiveCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective/AchieveObjectiveCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UnachieveObjective/UnachieveObjectiveCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UnachieveObjective/UnachieveObjectiveCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/AchieveObjectiveCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/UnachieveObjectiveCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.{GetByIdForTenantAsync, GetTrackedActiveDirectChildrenAsync, Update}`, `IObjectiveChangeRequestRepository.{HasPendingForObjectiveAsync, AddAsync}`, `IMilestoneMembershipCoordinator.{GetActiveAssigneeAsync, UpsertMembershipAsync, DeactivateMembershipAsync, HasOtherActiveAccessAsync}`, `IUnitOfWork.ExecuteInTransactionAsync`.
- Produces: `AchieveObjectiveCommand(Guid ObjectiveId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>`, `UnachieveObjectiveCommand(Guid ObjectiveId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>` — reuses `ObjectiveChangeOutcomeResponse` (already defined by the original milestone-hierarchy plan's Task 7) since both are "applied immediately or created a pending request" outcomes.

Same creator-vs-non-creator split as Delete/Transfer (design §6): caller must be the current Head; if also the creator, applies immediately; otherwise creates a `pending` `achieve`/`unachieve` change request routed to `ReportingManagerId`. Achieve additionally requires every direct child to already be `IsAchieved` — checked before the creator split, since neither branch can proceed without it. Unachieve has no precondition (always reversible) but does re-establish the Head's membership on immediate apply, mirroring Achieve's own membership cleanup in reverse.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AchieveObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective SubObjective(Guid createdById, bool isDefault = false, bool isAchieved = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = isDefault, Title = "Sub",
        OwnerId = HeadId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = true, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (AchieveObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IObjectiveChangeRequestRepository> Requests, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandler(
        Objective? objective, List<Objective>? unachievedChildren = null, bool hasPending = false, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unachievedChildren ?? new List<Objective>());

        var requests = new Mock<IObjectiveChangeRequestRepository>();
        requests.Setup(x => x.HasPendingForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(hasPending);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.HasOtherActiveAccessAsync(TenantId, ProjectId, HeadId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AchieveObjectiveCommandHandler(currentUser.Object, objectives.Object, requests.Object, membership.Object, unitOfWork.Object);
        return (handler, objectives, requests, membership);
    }

    [Fact]
    public async Task Handle_CreatorHeadAchieves_AppliesImmediately()
    {
        var (handler, objectives, requests, membership) = BuildHandler(SubObjective(createdById: HeadId));

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.IsAchieved && o.AchievedAt != null)), Times.Once);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ObjectiveId, HeadId, It.IsAny<CancellationToken>()), Times.Once);
        requests.Verify(x => x.AddAsync(It.IsAny<ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonCreatorHeadAchieves_CreatesPendingRequest()
    {
        var (handler, objectives, requests, _) = BuildHandler(SubObjective(createdById: OtherUserId));

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.IsAny<Objective>()), Times.Never);
        requests.Verify(x => x.AddAsync(It.Is<ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities.ObjectiveChangeRequest>(
            r => r.RequestType == "achieve"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DirectChildNotAchieved_ReturnsBadRequest()
    {
        var unachievedChild = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, IsAchieved = false, IsActive = true };
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: HeadId), unachievedChildren: new List<Objective> { unachievedChild });

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyAchieved_ReturnsConflict()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: HeadId, isAchieved: true));

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), callerId: OtherUserId);

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsBadRequest()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: HeadId, isDefault: true));

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyPendingRequest_ReturnsConflict()
    {
        var (handler, _, _, _) = BuildHandler(SubObjective(createdById: OtherUserId), hasPending: true);

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _, _, _) = BuildHandler(null);

        var result = await handler.Handle(new AchieveObjectiveCommand(ObjectiveId), CancellationToken.None);

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
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UnachieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class UnachieveObjectiveCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static Objective AchievedSubObjective(Guid createdById, bool isDefault = false) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = isDefault, Title = "Sub",
        OwnerId = HeadId, ReportingManagerId = createdById, CreatedById = createdById, IsActive = true,
        IsAchieved = true, AchievedAt = DateTimeOffset.UtcNow,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (UnachieveObjectiveCommandHandler Handler, Mock<IObjectiveRepository> Objectives, Mock<IMilestoneMembershipCoordinator> Membership) BuildHandler(
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

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, HeadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = HeadId, EmploymentStatusId = EmploymentStatusIds.Active });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ObjectiveChangeOutcomeResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UnachieveObjectiveCommandHandler(currentUser.Object, objectives.Object, requests.Object, membership.Object, unitOfWork.Object);
        return (handler, objectives, membership);
    }

    [Fact]
    public async Task Handle_CreatorHeadUnachieves_AppliesImmediatelyAndRestoresMembership()
    {
        var (handler, objectives, membership) = BuildHandler(AchievedSubObjective(createdById: HeadId));

        var result = await handler.Handle(new UnachieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Applied);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => !o.IsAchieved && o.AchievedAt == null)), Times.Once);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, HeadId, EmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAchieved_ReturnsConflict()
    {
        var objective = AchievedSubObjective(createdById: HeadId);
        objective.IsAchieved = false;
        var (handler, _, _) = BuildHandler(objective);

        var result = await handler.Handle(new UnachieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(AchievedSubObjective(createdById: OtherUserId), callerId: OtherUserId);

        var result = await handler.Handle(new UnachieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null);

        var result = await handler.Handle(new UnachieveObjectiveCommand(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AchieveObjectiveCommandHandlerTests|FullyQualifiedName~UnachieveObjectiveCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: `AchieveObjectiveCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;

public sealed record AchieveObjectiveCommand(Guid ObjectiveId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>;
```

- [ ] **Step 4: `AchieveObjectiveCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;

public class AchieveObjectiveCommandHandler : IRequestHandler<AchieveObjectiveCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public AchieveObjectiveCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IObjectiveChangeRequestRepository changeRequests,
        IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(AchieveObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("Use the Project achieve endpoint for the Default Objective.");

        if (objective.IsAchieved)
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("Objective is already achieved.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can achieve it.");

        // Precondition (design §6): every direct child must already be achieved. Shallow check -
        // grandchildren are covered transitively, since a child can't itself be achieved until
        // ITS children are.
        var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, ct);
        if (directChildren.Any(c => !c.IsAchieved))
            return Result<ObjectiveChangeOutcomeResponse>.Failure("All sub-milestones must be achieved before this one can be.");

        if (objective.CreatedById == userId)
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                objective.IsAchieved = true;
                objective.AchievedAt = now;
                objective.UpdatedAt = now;
                _objectives.Update(objective);

                // Freezing drops the Head's active participation on this milestone (design §6) -
                // same outgoing-access pattern as Transfer step 6, just with no new Head to
                // upsert a membership for.
                await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, innerCt);
                await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, objective.OwnerId, objective.Id, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);

                return Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(Applied: true, PendingRequest: null));
            }, ct);
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Achieve,
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

- [ ] **Step 5: `UnachieveObjectiveCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.UnachieveObjective;

public sealed record UnachieveObjectiveCommand(Guid ObjectiveId) : IRequest<Result<ObjectiveChangeOutcomeResponse>>;
```

- [ ] **Step 6: `UnachieveObjectiveCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.UnachieveObjective;

public class UnachieveObjectiveCommandHandler : IRequestHandler<UnachieveObjectiveCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public UnachieveObjectiveCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IObjectiveChangeRequestRepository changeRequests,
        IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(UnachieveObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("Use the Project achieve endpoint for the Default Objective.");

        if (!objective.IsAchieved)
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("Objective is not achieved.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can un-achieve it.");

        if (objective.CreatedById == userId)
        {
            var headAssignee = await _membership.GetActiveAssigneeAsync(tenantId, objective.OwnerId, ct);
            if (headAssignee is null)
                return Result<ObjectiveChangeOutcomeResponse>.Failure("The current head must be an active employee in this tenant.");

            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                objective.IsAchieved = false;
                objective.AchievedAt = null;
                objective.UpdatedAt = DateTimeOffset.UtcNow;
                _objectives.Update(objective);

                // Un-freezing restores the Head's active participation, mirroring Achieve's own
                // cleanup in reverse.
                await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, headAssignee.Id, innerCt);

                await _unitOfWork.SaveChangesAsync(innerCt);

                return Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(Applied: true, PendingRequest: null));
            }, ct);
        }

        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Unachieve,
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

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AchieveObjectiveCommandHandlerTests|FullyQualifiedName~UnachieveObjectiveCommandHandlerTests"`
Expected: PASS (8/8 + 4/4).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UnachieveObjective tests/ONEVO.Tests.Unit/Features/WorkManagement/AchieveObjectiveCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/UnachieveObjectiveCommandHandlerTests.cs
git commit -m "feat(work-management): add Achieve/UnachieveObjective vertical slices"
```

---

### Task 10: `AchieveProjectCommand` + `UnachieveProjectCommand` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AchieveProject/AchieveProjectCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AchieveProject/AchieveProjectCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/UnachieveProject/UnachieveProjectCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/UnachieveProject/UnachieveProjectCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/AchieveProjectCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/UnachieveProjectCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectRepository.{GetByIdForTenantAsync, Update}`, `IObjectiveRepository.{GetDefaultByProjectIdAsync, GetTrackedActiveDirectChildrenAsync}`, `IUnitOfWork.SaveChangesAsync`.
- Produces: `AchieveProjectCommand(Guid ProjectId) : IRequest<Result>`, `UnachieveProjectCommand(Guid ProjectId) : IRequest<Result>` — consumed by Task 15's controller.

Lead-only, always immediate — no approval path, matching the already-shipped root-of-tree exception `DeleteProjectCommandHandler`/`EditProjectCommandHandler` already use (the Project has no Reporting Manager to route a request to). Precondition checks the Default Objective's own direct children (design §6).

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.AchieveProject;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AchieveProjectCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid DefaultObjectiveId = Guid.NewGuid();

    private static Project ActiveProject(Guid leadId, bool isAchieved = false) => new()
    {
        Id = ProjectId, TenantId = TenantId, LeadId = leadId, IsActive = true, IsAchieved = isAchieved,
        Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective DefaultObjective() => new()
    {
        Id = DefaultObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, Title = "P",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (AchieveProjectCommandHandler Handler, Mock<IProjectRepository> Projects) BuildHandler(
        Project? project, List<Objective>? unachievedChildren = null, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(DefaultObjective());
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, DefaultObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unachievedChildren ?? new List<Objective>());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AchieveProjectCommandHandler(currentUser.Object, projects.Object, objectives.Object, unitOfWork.Object);
        return (handler, projects);
    }

    [Fact]
    public async Task Handle_LeadAchieves_AppliesImmediately()
    {
        var (handler, projects) = BuildHandler(ActiveProject(leadId: UserId));

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.Update(It.Is<Project>(p => p.IsAchieved && p.AchievedAt != null)), Times.Once);
    }

    [Fact]
    public async Task Handle_DirectChildNotAchieved_ReturnsBadRequest()
    {
        var unachievedChild = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, IsAchieved = false, IsActive = true };
        var (handler, _) = BuildHandler(ActiveProject(leadId: UserId), unachievedChildren: new List<Objective> { unachievedChild });

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyAchieved_ReturnsConflict()
    {
        var (handler, _) = BuildHandler(ActiveProject(leadId: UserId, isAchieved: true));

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NonLeadCaller_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ActiveProject(leadId: OtherUserId));

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new AchieveProjectCommand(ProjectId), CancellationToken.None);

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
using ONEVO.Application.Features.WorkManagement.Projects.Commands.UnachieveProject;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class UnachieveProjectCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Project AchievedProject(Guid leadId) => new()
    {
        Id = ProjectId, TenantId = TenantId, LeadId = leadId, IsActive = true, IsAchieved = true, AchievedAt = DateTimeOffset.UtcNow,
        Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private (UnachieveProjectCommandHandler Handler, Mock<IProjectRepository> Projects) BuildHandler(Project? project, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UnachieveProjectCommandHandler(currentUser.Object, projects.Object, unitOfWork.Object);
        return (handler, projects);
    }

    [Fact]
    public async Task Handle_LeadUnachieves_AppliesImmediately()
    {
        var (handler, projects) = BuildHandler(AchievedProject(leadId: UserId));

        var result = await handler.Handle(new UnachieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.Update(It.Is<Project>(p => !p.IsAchieved && p.AchievedAt == null)), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAchieved_ReturnsConflict()
    {
        var project = AchievedProject(leadId: UserId);
        project.IsAchieved = false;
        var (handler, _) = BuildHandler(project);

        var result = await handler.Handle(new UnachieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NonLeadCaller_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(AchievedProject(leadId: OtherUserId));

        var result = await handler.Handle(new UnachieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new UnachieveProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AchieveProjectCommandHandlerTests|FullyQualifiedName~UnachieveProjectCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: `AchieveProjectCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.AchieveProject;

public sealed record AchieveProjectCommand(Guid ProjectId) : IRequest<Result>;
```

- [ ] **Step 4: `AchieveProjectCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.AchieveProject;

public class AchieveProjectCommandHandler : IRequestHandler<AchieveProjectCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;

    public AchieveProjectCommandHandler(
        ICurrentUser currentUser, IProjectRepository projects, IObjectiveRepository objectives, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _projects = projects;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AchieveProjectCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result.NotFound("Project not found.");

        if (project.LeadId != userId)
            return Result.Forbidden("Only the project lead can achieve this project.");

        if (project.IsAchieved)
            return Result.Conflict("Project is already achieved.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result.NotFound("Default objective not found for this project.");

        var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, defaultObjective.Id, ct);
        if (directChildren.Any(c => !c.IsAchieved))
            return Result.Failure("All top-level milestones must be achieved before the project can be.");

        project.IsAchieved = true;
        project.AchievedAt = DateTimeOffset.UtcNow;
        project.UpdatedAt = DateTimeOffset.UtcNow;

        _projects.Update(project);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 5: `UnachieveProjectCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.UnachieveProject;

public sealed record UnachieveProjectCommand(Guid ProjectId) : IRequest<Result>;
```

- [ ] **Step 6: `UnachieveProjectCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.UnachieveProject;

public class UnachieveProjectCommandHandler : IRequestHandler<UnachieveProjectCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;

    public UnachieveProjectCommandHandler(ICurrentUser currentUser, IProjectRepository projects, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _projects = projects;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UnachieveProjectCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result.NotFound("Project not found.");

        if (project.LeadId != userId)
            return Result.Forbidden("Only the project lead can un-achieve this project.");

        if (!project.IsAchieved)
            return Result.Conflict("Project is not achieved.");

        project.IsAchieved = false;
        project.AchievedAt = null;
        project.UpdatedAt = DateTimeOffset.UtcNow;

        _projects.Update(project);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AchieveProjectCommandHandlerTests|FullyQualifiedName~UnachieveProjectCommandHandlerTests"`
Expected: PASS (5/5 + 4/4).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AchieveProject src/ONEVO.Application/Features/WorkManagement/Projects/Commands/UnachieveProject tests/ONEVO.Tests.Unit/Features/WorkManagement/AchieveProjectCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/UnachieveProjectCommandHandlerTests.cs
git commit -m "feat(work-management): add Achieve/UnachieveProject vertical slices"
```

---

### Task 11: Wire Achieve into `EditObjectiveCommandHandler` (freeze check) and `ApproveObjectiveChangeRequestCommandHandler` (new switch arms + Transfer's membership sync)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective/EditObjectiveCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EditObjectiveCommandHandlerTests.cs` (extend)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ApproveObjectiveChangeRequestCommandHandlerTests.cs` (extend)

**Interfaces:**
- Consumes: `IMilestoneMembershipCoordinator` (Task 4), `IObjectiveRepository.GetTrackedActiveDirectChildrenAsync` (Task 3).

**Two real gaps this task closes, stated plainly:**
1. Edit never checked `!objective.IsAchieved` — an achieved milestone was still editable. Same one-line guard pattern as the existing `!objective.IsActive` check.
2. The ALREADY-SHIPPED `case ObjectiveChangeRequestTypes.Transfer:` branch in `ApproveObjectiveChangeRequestCommandHandler` only reassigns `OwnerId` — it never ran the membership sync or Reporting Manager cascade Task 7 added to the *immediate*-apply Transfer path. Design §3 requires both paths (immediate and approved) to do the identical membership work — this task brings the approval path up to parity, not just adds the two new `achieve`/`unachieve` arms.

- [ ] **Step 1: Add the freeze-check test to `EditObjectiveCommandHandlerTests.cs`**

```csharp
    [Fact]
    public async Task Handle_ObjectiveAchieved_ReturnsBadRequest()
    {
        var achieved = SubObjective(createdById: OtherUserId);
        achieved.IsAchieved = true;
        var (handler, _, _) = BuildHandler(achieved, ParentObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter EditObjectiveCommandHandlerTests`
Expected: the new test FAILs (returns 200/404, not 400) — everything else still passes.

- [ ] **Step 3: Add the freeze check to `EditObjectiveCommandHandler`**

In the existing `Handle` method, change:

```csharp
        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveEditOutcomeResponse>.NotFound("Objective not found.");

        // Default-Objective carve-out (design §5) - edited only via PUT /projects/{id}.
        if (objective.IsDefault)
            return Result<ObjectiveEditOutcomeResponse>.Failure("Use the Project edit endpoint for the Default Objective.");
```

to:

```csharp
        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveEditOutcomeResponse>.NotFound("Objective not found.");

        // Default-Objective carve-out (design §5) - edited only via PUT /projects/{id}.
        if (objective.IsDefault)
            return Result<ObjectiveEditOutcomeResponse>.Failure("Use the Project edit endpoint for the Default Objective.");

        if (objective.IsAchieved)
            return Result<ObjectiveEditOutcomeResponse>.Failure("An achieved milestone cannot be edited.");
```

(Nothing else in the handler changes.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter EditObjectiveCommandHandlerTests`
Expected: PASS (all original tests + the new one).

- [ ] **Step 5: Add the new tests to `ApproveObjectiveChangeRequestCommandHandlerTests.cs`**

Extend `BuildHandler` in the existing file to also construct/inject `IMilestoneMembershipCoordinator` (default: assignee resolves to a valid active `Employee`, `HasOtherActiveAccessAsync` returns `false`) so the existing Delete/Transfer/Edit-approval tests keep passing unmodified, then add:

```csharp
    [Fact]
    public async Task Handle_ApproveTransfer_SyncsMembershipAndCascadesReportingManager()
    {
        var child = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ParentObjectiveId = ObjectiveId, IsActive = true, ReportingManagerId = Guid.NewGuid() };
        var (handler, objectives, membership) = BuildHandlerWithMembership(TransferRequest(), TargetObjective(), directChildren: new List<Objective> { child });

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewHeadId, child.ReportingManagerId);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, It.IsAny<Guid>(), ObjectiveId, NewHeadId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ApproveAchieve_SetsIsAchievedAndDeactivatesHeadMembership()
    {
        var achieveRequest = new ObjectiveChangeRequest
        {
            Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Achieve,
            ReportingManagerId = ManagerId, Status = ObjectiveChangeRequestStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objectives, membership) = BuildHandlerWithMembership(achieveRequest, TargetObjective());

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.IsAchieved && o.AchievedAt != null)), Times.Once);
        membership.Verify(x => x.DeactivateMembershipAsync(TenantId, It.IsAny<Guid>(), ObjectiveId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ApproveUnachieve_ClearsIsAchievedAndRestoresHeadMembership()
    {
        var unachieveRequest = new ObjectiveChangeRequest
        {
            Id = RequestId, TenantId = TenantId, ObjectiveId = ObjectiveId, RequestType = ObjectiveChangeRequestTypes.Unachieve,
            ReportingManagerId = ManagerId, Status = ObjectiveChangeRequestStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
        };
        var achievedTarget = TargetObjective();
        achievedTarget.IsAchieved = true;
        achievedTarget.AchievedAt = DateTimeOffset.UtcNow;
        var (handler, objectives, membership) = BuildHandlerWithMembership(unachieveRequest, achievedTarget);

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => !o.IsAchieved && o.AchievedAt == null)), Times.Once);
        membership.Verify(x => x.UpsertMembershipAsync(TenantId, It.IsAny<Guid>(), ObjectiveId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }
```

Add a `BuildHandlerWithMembership` overload (mirroring the existing `BuildHandler`, extended with an optional `directChildren` parameter defaulting to an empty list, and injecting a `Mock<IMilestoneMembershipCoordinator>` whose `GetActiveAssigneeAsync` returns a valid active `Employee` by default) alongside the file's existing `BuildHandler` — do not remove `BuildHandler` itself, since the original Delete/Transfer/Edit-approval tests still call it and must keep passing unmodified. Add `objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(...))` returning `directChildren` to the new overload.

- [ ] **Step 6: Run tests to verify the new ones fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ApproveObjectiveChangeRequestCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 7: Update `ApproveObjectiveChangeRequestCommandHandler`**

```csharp
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;

public class ApproveObjectiveChangeRequestCommandHandler : IRequestHandler<ApproveObjectiveChangeRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveObjectiveChangeRequestCommandHandler(
        ICurrentUser currentUser, IObjectiveChangeRequestRepository changeRequests,
        IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _changeRequests = changeRequests;
        _objectives = objectives;
        _membership = membership;
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

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
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
                    var oldHeadUserId = objective.OwnerId;
                    var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, transferPayload.NewHeadUserId, innerCt);
                    if (newHeadAssignee is null)
                        return Result.Failure("The new head must be an active employee in this tenant.");

                    objective.OwnerId = transferPayload.NewHeadUserId;
                    objective.UpdatedAt = now;

                    var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                    foreach (var child in directChildren)
                    {
                        child.ReportingManagerId = transferPayload.NewHeadUserId;
                        child.UpdatedAt = now;
                    }

                    await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, transferPayload.NewHeadUserId, newHeadAssignee.Id, innerCt);
                    await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadUserId, innerCt);
                    await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadUserId, objective.Id, innerCt);
                    break;

                case ObjectiveChangeRequestTypes.Achieve:
                    objective.IsAchieved = true;
                    objective.AchievedAt = now;
                    objective.UpdatedAt = now;
                    await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, innerCt);
                    await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, objective.OwnerId, objective.Id, innerCt);
                    break;

                case ObjectiveChangeRequestTypes.Unachieve:
                    var headAssignee = await _membership.GetActiveAssigneeAsync(tenantId, objective.OwnerId, innerCt);
                    if (headAssignee is null)
                        return Result.Failure("The current head must be an active employee in this tenant.");

                    objective.IsAchieved = false;
                    objective.AchievedAt = null;
                    objective.UpdatedAt = now;
                    await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, headAssignee.Id, innerCt);
                    break;
            }

            _objectives.Update(objective);

            changeRequest.Status = ObjectiveChangeRequestStatuses.Approved;
            changeRequest.DecidedAt = now;
            changeRequest.DecidedById = userId;
            _changeRequests.Update(changeRequest);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result.Success();
        }, ct);
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ApproveObjectiveChangeRequestCommandHandlerTests|FullyQualifiedName~EditObjectiveCommandHandlerTests"`
Expected: PASS (all original tests + 4 new: 1 Edit freeze-check + 3 Approve).

- [ ] **Step 9: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: all tests pass, no regressions from the `ApproveObjectiveChangeRequestCommandHandler` constructor signature change (it now takes one more dependency — any other test file constructing it directly, if any exist beyond its own test file, needs the same update; a `dotnet build` before running tests will surface any such site as a compile error).

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective/EditObjectiveCommandHandler.cs src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EditObjectiveCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/ApproveObjectiveChangeRequestCommandHandlerTests.cs
git commit -m "feat(work-management): freeze Edit on achieved milestones; sync membership and cascade RM on approved Transfer; wire Achieve/Unachieve into Approve"
```

---

### Task 12: `GetObjectiveByIdQuery` vertical slice (unit-tested)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs` — already exists (Task 5), no change here.
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.GetByIdForTenantAsync` (existing), `IProjectMemberRepository.HasActiveMembershipForAnyObjectiveAsync` (new, this task), `IPermissionResolver.ResolveAsync` (existing).
- Produces: `GetObjectiveByIdQuery(Guid ObjectiveId) : IRequest<Result<ObjectiveDetailResponse>>` — consumed by Task 15's controller.

**Design §5 authorization, restated precisely:** `projects:read`/`*` grants access outright. Otherwise, the caller needs an active membership on the target Objective itself OR on any of its ancestors (walking `ParentObjectiveId` up to the Default Objective). A "direct" (Default-Objective-scoped) membership is not a separate check — the Default Objective is always an ancestor of every non-default node, and IS the target itself when fetching the Default Objective directly, so the ancestor-or-self walk already subsumes it.

- [ ] **Step 1: `IProjectMemberRepository` addition**

Add to the existing interface:

```csharp
    /// <summary>
    /// True if the user has an active membership row scoped to any of the given ObjectiveIds -
    /// used for the "self or any ancestor" visibility check (design §5). Callers pass the target
    /// Objective's own Id plus its full ancestor chain.
    /// </summary>
    Task<bool> HasActiveMembershipForAnyObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, IReadOnlyList<Guid> objectiveIds, CancellationToken ct = default);
```

- [ ] **Step 2: `EfProjectMemberRepository` implementation**

```csharp
    public async Task<bool> HasActiveMembershipForAnyObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, IReadOnlyList<Guid> objectiveIds, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId
                        && m.IsActive && objectiveIds.Contains(m.ObjectiveId), ct);
    }
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 4: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveByIdQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective Target(bool isActive = true) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ParentId, IsActive = isActive,
        Title = "Sub", OwnerId = Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective Parent() => new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = null, IsDefault = true, IsActive = true,
        Title = "Default", OwnerId = Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveByIdQueryHandler Handler, Mock<IProjectMemberRepository> Members) BuildHandler(
        Objective? target, List<string> permissions, bool hasAncestorOrSelfMembership)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(Parent());

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, UserId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasAncestorOrSelfMembership);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(permissions);

        var handler = new GetObjectiveByIdQueryHandler(currentUser.Object, objectives.Object, members.Object, permissionResolver.Object);
        return (handler, members);
    }

    [Fact]
    public async Task Handle_HasReadPermission_SucceedsWithoutCheckingMembership()
    {
        var (handler, members) = BuildHandler(Target(), ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        members.Verify(x => x.HasActiveMembershipForAnyObjectiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoPermissionButAncestorOrSelfMembership_Succeeds()
    {
        var (handler, _) = BuildHandler(Target(), [], hasAncestorOrSelfMembership: true);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_MembershipCheckIncludesTargetAndAncestorIds()
    {
        var (handler, members) = BuildHandler(Target(), [], hasAncestorOrSelfMembership: true);

        await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        members.Verify(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, UserId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(ObjectiveId) && ids.Contains(ParentId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoPermissionAndNoMembership_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(Target(), [], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveObjective_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(Target(isActive: false), ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null, ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetObjectiveByIdQueryHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 6: `GetObjectiveByIdQuery`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;

public sealed record GetObjectiveByIdQuery(Guid ObjectiveId) : IRequest<Result<ObjectiveDetailResponse>>;
```

- [ ] **Step 7: `GetObjectiveByIdQueryHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;

public class GetObjectiveByIdQueryHandler : IRequestHandler<GetObjectiveByIdQuery, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;

    public GetObjectiveByIdQueryHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IProjectMemberRepository members, IPermissionResolver permissionResolver)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _members = members;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(GetObjectiveByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var selfAndAncestorIds = new List<Guid> { objective.Id };
            var cursor = objective;
            while (cursor.ParentObjectiveId is not null)
            {
                var parent = await _objectives.GetByIdForTenantAsync(tenantId, cursor.ParentObjectiveId.Value, ct);
                if (parent is null)
                    break;

                selfAndAncestorIds.Add(parent.Id);
                cursor = parent;
            }

            var hasAccess = await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId, userId, selfAndAncestorIds, ct);
            if (!hasAccess)
                return Result<ObjectiveDetailResponse>.Forbidden("You do not have access to this milestone.");
        }

        return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetObjectiveByIdQueryHandlerTests`
Expected: PASS (6/6).

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs
git commit -m "feat(work-management): add GetObjectiveByIdQuery vertical slice"
```

---

### Task 13: `GetObjectiveTreeQueryHandler` — subtree-scoping for milestone-only members (unit-tested)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveTreeQueryHandlerTests.cs` (extend)

**Interfaces:**
- Consumes: `IProjectMemberRepository.GetActiveObjectiveIdsForUserInProjectAsync` (new, this task), `HasActiveMembershipForAnyObjectiveAsync` (Task 12).

**Design §5 algorithm:** a "direct" member (active membership on the Default Objective) still sees the whole tree — unchanged. A milestone-scoped-only member sees the union, across every milestone they're a member of, of that milestone's ancestor chain (context) plus its full descendant subtree — computed in memory from the single already-fetched flat list (no extra round-trips per node), since the whole active tree is small enough per project to hold at once.

- [ ] **Step 1: `IProjectMemberRepository` addition**

```csharp
    /// <summary>All ObjectiveIds this user has an active membership on, within this project.</summary>
    Task<IReadOnlyList<Guid>> GetActiveObjectiveIdsForUserInProjectAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default);
```

- [ ] **Step 2: `EfProjectMemberRepository` implementation**

```csharp
    public async Task<IReadOnlyList<Guid>> GetActiveObjectiveIdsForUserInProjectAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId && m.IsActive)
            .Select(m => m.ObjectiveId)
            .ToListAsync(ct);
    }
```

- [ ] **Step 3: Extend the existing test file**

Add to `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveTreeQueryHandlerTests.cs` (read the file first — it already has 3 tests from the original milestone-hierarchy plan's Task 11; extend `BuildHandler` to also stub `GetActiveObjectiveIdsForUserInProjectAsync` and `HasActiveMembershipForAnyObjectiveAsync`, defaulting both so the 3 existing tests — which exercise the direct-member/full-tree path — keep passing unmodified):

```csharp
    [Fact]
    public async Task Handle_MilestoneScopedMember_ReturnsOnlyOwnSubtreePlusAncestors()
    {
        var defaultObjective = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true };
        var myMilestone = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = defaultObjective.Id, IsActive = true };
        var myChild = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = myMilestone.Id, IsActive = true };
        var unrelatedSibling = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = defaultObjective.Id, IsActive = true };

        var (handler, _) = BuildHandler(
            Project(), new List<Objective> { defaultObjective, myMilestone, myChild, unrelatedSibling },
            isActiveMember: true, hasDirectMembership: false, ownedObjectiveIds: new List<Guid> { myMilestone.Id });

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var returnedIds = result.Value!.Select(o => o.Id).ToHashSet();
        Assert.Contains(defaultObjective.Id, returnedIds); // ancestor context
        Assert.Contains(myMilestone.Id, returnedIds);       // self
        Assert.Contains(myChild.Id, returnedIds);            // descendant
        Assert.DoesNotContain(unrelatedSibling.Id, returnedIds); // NOT a sibling branch
    }

    [Fact]
    public async Task Handle_DirectMember_StillSeesFullTree()
    {
        var defaultObjective = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true };
        var someMilestone = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = defaultObjective.Id, IsActive = true };

        var (handler, _) = BuildHandler(
            Project(), new List<Objective> { defaultObjective, someMilestone },
            isActiveMember: true, hasDirectMembership: true, ownedObjectiveIds: new List<Guid>());

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }
```

Update the file's `BuildHandler` signature to accept `bool hasDirectMembership` and `List<Guid> ownedObjectiveIds`, stubbing the two new repository methods accordingly, and update all 3 pre-existing test call sites to pass `hasDirectMembership: true, ownedObjectiveIds: new List<Guid>()` (preserving their original full-tree-visible behavior).

- [ ] **Step 4: Run tests to verify the new ones fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetObjectiveTreeQueryHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 5: Update `GetObjectiveTreeQueryHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;

public class GetObjectiveTreeQueryHandler : IRequestHandler<GetObjectiveTreeQuery, Result<IReadOnlyList<ObjectiveTreeItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;

    public GetObjectiveTreeQueryHandler(
        ICurrentUser currentUser, IProjectRepository projects, IProjectMemberRepository members, IObjectiveRepository objectives)
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
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("You do not have access to this project.");

        var allObjectives = await _objectives.GetTreeByProjectIdAsync(tenantId, project.Id, ct);

        var defaultObjective = allObjectives.FirstOrDefault(o => o.IsDefault);
        var hasDirectMembership = defaultObjective is not null
            && await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, project.Id, userId, new[] { defaultObjective.Id }, ct);

        if (hasDirectMembership)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Success(allObjectives.Select(ObjectiveMapper.ToTreeItem).ToList());

        var ownedObjectiveIds = await _members.GetActiveObjectiveIdsForUserInProjectAsync(tenantId, project.Id, userId, ct);

        var byId = allObjectives.ToDictionary(o => o.Id);
        var childrenByParent = allObjectives
            .Where(o => o.ParentObjectiveId is not null)
            .GroupBy(o => o.ParentObjectiveId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var reachable = new HashSet<Guid>();
        foreach (var ownedId in ownedObjectiveIds)
        {
            if (!byId.TryGetValue(ownedId, out var owned))
                continue;

            reachable.Add(owned.Id);

            var cursor = owned;
            while (cursor.ParentObjectiveId is not null && byId.TryGetValue(cursor.ParentObjectiveId.Value, out var parent))
            {
                reachable.Add(parent.Id);
                cursor = parent;
            }

            var queue = new Queue<Guid>();
            queue.Enqueue(owned.Id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!childrenByParent.TryGetValue(current, out var children))
                    continue;

                foreach (var child in children)
                {
                    if (reachable.Add(child.Id))
                        queue.Enqueue(child.Id);
                }
            }
        }

        var scoped = allObjectives.Where(o => reachable.Contains(o.Id)).Select(ObjectiveMapper.ToTreeItem).ToList();
        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Success(scoped);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetObjectiveTreeQueryHandlerTests`
Expected: PASS (5/5 — 3 original + 2 new).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveTreeQueryHandlerTests.cs
git commit -m "feat(work-management): scope GetObjectiveTree to the caller's reachable subtree"
```

---

### Task 14: `GetMyObjectiveHistoryQuery` vertical slice (unit-tested)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveHistoryItemResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyObjectiveHistory/GetMyObjectiveHistoryQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyObjectiveHistory/GetMyObjectiveHistoryQueryHandler.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveHistoryItemViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`

**Interfaces:**
- Consumes: `IProjectMemberRepository.ListInactiveMembershipsForUserAsync` (new, this task), `IObjectiveRepository.GetByIdForTenantAsync`.
- Produces: `GetMyObjectiveHistoryQuery() : IRequest<Result<IReadOnlyList<ObjectiveHistoryItemResponse>>>` — consumed by Task 15's controller.

Read-only, design §5: milestones the caller used to have active access to (Head or member) but no longer does — because they were Transferred away, removed as a member, or the milestone was Achieved and they had no other reason to stay in the project. Sourced from `project_members` rows where `IsActive = false` (the same rows every deactivation in this plan already produces — Tasks 7/8/9/11 all set `RemovedAt` when deactivating). No write actions are exposed from this endpoint.

- [ ] **Step 1: `IProjectMemberRepository` addition**

```csharp
    /// <summary>Every deactivated (IsActive = false, RemovedAt set) membership row for this user, across all projects in the tenant - the raw material for the "milestones I used to participate in" history view.</summary>
    Task<IReadOnlyList<ProjectMember>> ListInactiveMembershipsForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
```

- [ ] **Step 2: `EfProjectMemberRepository` implementation**

```csharp
    public async Task<IReadOnlyList<ProjectMember>> ListInactiveMembershipsForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.UserId == userId && !m.IsActive && m.RemovedAt != null)
            .OrderByDescending(m => m.RemovedAt)
            .ToListAsync(ct);
    }
```

- [ ] **Step 3: `ObjectiveHistoryItemResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveHistoryItemResponse(
    Guid ObjectiveId, string Title, Guid ProjectId, bool IsAchieved, DateTimeOffset? RemovedAt);
```

- [ ] **Step 4: Verify build so far**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 5: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyObjectiveHistory;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetMyObjectiveHistoryQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (GetMyObjectiveHistoryQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        List<ProjectMember> inactiveMemberships, Objective? objective)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListInactiveMembershipsForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(inactiveMemberships);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var handler = new GetMyObjectiveHistoryQueryHandler(currentUser.Object, members.Object, objectives.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_HasInactiveMembership_ReturnsHistoryItem()
    {
        var removedAt = DateTimeOffset.UtcNow;
        var membership = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, IsActive = false, RemovedAt = removedAt };
        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, Title = "Old Milestone", IsAchieved = true };

        var (handler, _) = BuildHandler(new List<ProjectMember> { membership }, objective);

        var result = await handler.Handle(new GetMyObjectiveHistoryQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal(ObjectiveId, item.ObjectiveId);
        Assert.Equal("Old Milestone", item.Title);
        Assert.True(item.IsAchieved);
        Assert.Equal(removedAt, item.RemovedAt);
    }

    [Fact]
    public async Task Handle_NoInactiveMemberships_ReturnsEmptyList()
    {
        var (handler, _) = BuildHandler(new List<ProjectMember>(), null);

        var result = await handler.Handle(new GetMyObjectiveHistoryQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_ObjectiveNoLongerExists_SkipsItSilently()
    {
        var membership = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, IsActive = false, RemovedAt = DateTimeOffset.UtcNow };
        var (handler, _) = BuildHandler(new List<ProjectMember> { membership }, null);

        var result = await handler.Handle(new GetMyObjectiveHistoryQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }
}
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetMyObjectiveHistoryQueryHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 7: `GetMyObjectiveHistoryQuery`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyObjectiveHistory;

public sealed record GetMyObjectiveHistoryQuery() : IRequest<Result<IReadOnlyList<ObjectiveHistoryItemResponse>>>;
```

- [ ] **Step 8: `GetMyObjectiveHistoryQueryHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyObjectiveHistory;

public class GetMyObjectiveHistoryQueryHandler : IRequestHandler<GetMyObjectiveHistoryQuery, Result<IReadOnlyList<ObjectiveHistoryItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;

    public GetMyObjectiveHistoryQueryHandler(ICurrentUser currentUser, IProjectMemberRepository members, IObjectiveRepository objectives)
    {
        _currentUser = currentUser;
        _members = members;
        _objectives = objectives;
    }

    public async Task<Result<IReadOnlyList<ObjectiveHistoryItemResponse>>> Handle(GetMyObjectiveHistoryQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ObjectiveHistoryItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ObjectiveHistoryItemResponse>>.Forbidden("Tenant context missing.");

        var inactiveMemberships = await _members.ListInactiveMembershipsForUserAsync(tenantId, userId, ct);

        var items = new List<ObjectiveHistoryItemResponse>();
        foreach (var membership in inactiveMemberships)
        {
            var objective = await _objectives.GetByIdForTenantAsync(tenantId, membership.ObjectiveId, ct);
            if (objective is null)
                continue;

            items.Add(new ObjectiveHistoryItemResponse(objective.Id, objective.Title, objective.ProjectId, objective.IsAchieved, membership.RemovedAt));
        }

        return Result<IReadOnlyList<ObjectiveHistoryItemResponse>>.Success(items);
    }
}
```

- [ ] **Step 9: `ObjectiveHistoryItemViewModel` + mapper**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveHistoryItemViewModel(
    Guid ObjectiveId, string Title, Guid ProjectId, bool IsAchieved, DateTimeOffset? RemovedAt);
```

Add to `ObjectiveViewModelMapper.cs`:

```csharp
    public static ObjectiveHistoryItemViewModel ToViewModel(this ObjectiveHistoryItemResponse dto) => new(
        dto.ObjectiveId, dto.Title, dto.ProjectId, dto.IsAchieved, dto.RemovedAt);
```

- [ ] **Step 10: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetMyObjectiveHistoryQueryHandlerTests`
Expected: PASS (3/3).

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveHistoryItemResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyObjectiveHistory src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveHistoryItemViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyObjectiveHistoryQueryHandlerTests.cs
git commit -m "feat(work-management): add GetMyObjectiveHistoryQuery vertical slice"
```

---

### Task 15: Controller wiring — `ObjectivesController` (6 new/changed actions) and `ProjectsController` (2 new actions)

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/RemoveObjectiveMemberRequest.cs` — not needed; `UserId` comes from the route, no body.

**Interfaces:**
- Consumes: every command/query from Tasks 6–14.
- Produces: the 8 new/changed HTTP routes this whole plan exists to ship.

**New route table (added to the 8 already shipped):**

| # | Method + Route | Handler | Permission |
|---|---|---|---|
| 1 | `POST /api/v1/work/objectives/{id:guid}/members` | `AddObjectiveMemberCommand` | `projects:access` |
| 2 | `DELETE /api/v1/work/objectives/{id:guid}/members/{userId:guid}` | `RemoveObjectiveMemberCommand` | `projects:access` |
| 3 | `POST /api/v1/work/objectives/{id:guid}/achieve` | `AchieveObjectiveCommand` | `projects:access` |
| 4 | `POST /api/v1/work/objectives/{id:guid}/unachieve` | `UnachieveObjectiveCommand` | `projects:access` |
| 5 | `GET /api/v1/work/objectives/{id:guid}` | `GetObjectiveByIdQuery` | none (permission-or-ancestor-membership, in-handler — matches `GetTree`/`GetProjectById`'s precedent) |
| 6 | `GET /api/v1/work/objectives/mine/history` | `GetMyObjectiveHistoryQuery` | `projects:access` |
| 7 | `POST /api/v1/work/projects/{id:guid}/achieve` | `AchieveProjectCommand` | `projects:access` |
| 8 | `POST /api/v1/work/projects/{id:guid}/unachieve` | `UnachieveProjectCommand` | `projects:access` |

Route disambiguation: `{id:guid}` (guid-constrained) vs literal segments (`change-requests`, `mine`) at the same position is already proven collision-free in this codebase (Slice 2/3 precedent, re-verified in Task 12's review). `mine/history` is two segments, `{id:guid}` is one — no possible ambiguity regardless.

- [ ] **Step 1: Replace `ObjectivesController.cs` in full**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Objectives;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UnachieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyObjectiveHistory;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work/objectives")]
[Authorize(Policy = "TenantPolicy")]
public class ObjectivesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ObjectivesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Creates a sub-milestone under an existing Objective. Caller must be the parent's current Head.</summary>
    [HttpPost]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Create([FromBody] CreateObjectiveRequest request, CancellationToken ct)
    {
        var command = new CreateObjectiveCommand(
            request.ParentObjectiveId, request.Title, request.Description,
            request.StartDate, request.EndDate, request.AllocatedHours, request.HeadUserId);

        var result = await _mediator.Send(command, ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Gets a single milestone by id. Permission-or-ancestor-membership, checked in-handler.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveByIdQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Edits a milestone. Non-conflicting edits apply immediately; edits that would conflict with the parent's date/hours constraints become a pending approval request unless the caller is the milestone's own creator. Frozen (400) once the milestone is Achieved.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("projects:access")]
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
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteObjectiveCommand(id), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Reassigns a milestone's head. Same immediate-vs-pending split as Delete. On applying, also syncs project membership for both heads and cascades ReportingManagerId to direct children.</summary>
    [HttpPost("{id:guid}/transfer")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferObjectiveHeadRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new TransferObjectiveHeadCommand(id, request.NewHeadUserId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Adds a member to this milestone. Head-only; the member becomes a project_members row scoped to this Objective. Does not grant projects:access (only assigning someone as Head does that).</summary>
    [HttpPost("{id:guid}/members")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddObjectiveMemberRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddObjectiveMemberCommand(id, request.UserId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Removes a member from this milestone. Head-only. Rejects removing the current head - use Transfer instead.</summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RemoveObjectiveMemberCommand(id, userId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Marks a milestone Achieved. Requires every direct sub-milestone to already be Achieved. Same immediate-vs-pending split as Delete.</summary>
    [HttpPost("{id:guid}/achieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Achieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new AchieveObjectiveCommand(id), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Reverts an Achieved milestone back to active. Same immediate-vs-pending split as Delete.</summary>
    [HttpPost("{id:guid}/unachieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Unachieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new UnachieveObjectiveCommand(id), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return result.Value!.Applied
            ? NoContent()
            : Accepted(result.Value.PendingRequest!.ToViewModel());
    }

    /// <summary>Approves a pending change request. Caller must be the request's Reporting Manager.</summary>
    [HttpPost("change-requests/{requestId:guid}/approve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ApproveChangeRequest(Guid requestId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ApproveObjectiveChangeRequestCommand(requestId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Rejects a pending change request. Caller must be the request's Reporting Manager. The Objective is left unchanged.</summary>
    [HttpPost("change-requests/{requestId:guid}/reject")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> RejectChangeRequest(Guid requestId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RejectObjectiveChangeRequestCommand(requestId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>The caller's own approval queue - pending requests where they are the Reporting Manager.</summary>
    [HttpGet("change-requests/mine")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ListMyChangeRequests(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListMyObjectiveChangeRequestsQuery(), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(r => r.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Milestones the caller used to have active access to but no longer does (Transferred away, removed as a member, or Achieved with no other reason to stay in the project). Read-only.</summary>
    [HttpGet("mine/history")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> MyHistory(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyObjectiveHistoryQuery(), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(h => h.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>The full Objective tree for a Project, scoped to what the caller can reach (design §5). No [RequirePermission] here on purpose: the handler checks membership fallback itself, matching GetProjectByIdQueryHandler's pattern.</summary>
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

- [ ] **Step 2: Add the two new actions to `ProjectsController.cs`**

Add inside the existing `ProjectsController` class, after `Delete` (read the current file first — do not touch `Create`/`Edit`/`GetById`/`ListMine`/`ListByUser`):

```csharp
    /// <summary>Marks a Project Achieved. Requires every top-level milestone (direct child of the Default Objective) to already be Achieved. Lead-only, always immediate - the Project is the tree's root, no approval routing.</summary>
    [HttpPost("{id:guid}/achieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Achieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new AchieveProjectCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Reverts an Achieved Project back to active. Lead-only, always immediate.</summary>
    [HttpPost("{id:guid}/unachieve")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Unachieve(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new UnachieveProjectCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the two new `using`s to the top of the file:

```csharp
using ONEVO.Application.Features.WorkManagement.Projects.Commands.AchieveProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.UnachieveProject;
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [ ] **Step 4: Run the full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: all tests pass, no regressions — every test from Tasks 1–14 plus every pre-existing test in the repo.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs
git commit -m "feat(work-management): wire member management, Achieve/Unachieve, GetById, and history endpoints"
```

---

### Task 16: Integration tests — full HTTP flow

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/Features/WorkManagement/CreateProjectEndpointTests.cs` (add new `[Fact]` methods to the existing class, same fixture as every prior Work Management integration test in this file)

**Interfaces:**
- Consumes: `_tenantA`, `_tenantACategoryId`, `SendCreateProjectAsync`, `SendCreateObjectiveAsync`, `ReadJsonAsync`, `BuildGetRequest` (all already present in this file from Slice 2/3).

**Scope decision, same reasoning as every prior integration-test task in this feature:** the fixture provisions exactly one authenticated user per tenant (the owner, who is Project Lead, Objective creator, and Objective Head all at once for anything they personally create). Every non-creator branch this plan adds (Transfer's/Achieve's pending-request path, a second person's member-add) needs a second authenticated-over-HTTP user, which this fixture doesn't build — already proven precisely at the handler-unit-test level in Tasks 6–14 (mocked repositories covering every branch). HTTP coverage below sticks to what one owner-per-tenant can reach for real: membership sync after Create, the (still-full-tree, since the owner is a direct member) scoped tree endpoint, member add/remove, Achieve/Unachieve applying immediately (owner is always creator of what they create), Project Achieve/Unachieve, GetObjectiveById, and GetMyObjectiveHistory.

- [ ] **Step 1: Add the new tests**

Add inside the `CreateProjectEndpointTests` class, after the milestone-hierarchy tests from the prior plan:

```csharp
    [Fact]
    public async Task CreateObjective_ByCallerDefaultingToHead_CreatesProjectMembership()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Membership Sync Target", "MST1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();

        var response = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Membership Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var objectiveId = (await ReadJsonAsync(response)).GetProperty("id").GetGuid();

        var getResponse = await _client.SendAsync(BuildGetRequest(_tenantA, $"/api/v1/work/objectives/{objectiveId}"));
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, "the caller (default Head) must already have membership-based access to what they just created");
    }

    [Fact]
    public async Task AddThenRemoveObjectiveMember_HeadManagesMembership()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Member Mgmt Target", "MMT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Member Mgmt Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();
        var ownerUserId = (await ReadJsonAsync(created)).GetProperty("creatorMembership").GetProperty("userId").GetGuid();

        var addResponse = await SendAddObjectiveMemberAsync(_tenantA, subId, ownerUserId);
        addResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await addResponse.Content.ReadAsStringAsync());

        var removeHeadResponse = await SendRemoveObjectiveMemberAsync(_tenantA, subId, ownerUserId);
        removeHeadResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, "cannot remove the current head as a member - use Transfer instead");
    }

    [Fact]
    public async Task AchieveObjective_ByCreatorHead_AppliesAndFreezesEdit()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Achieve Milestone Target", "AMT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var sub = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Achievable Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m);
        var subId = (await ReadJsonAsync(sub)).GetProperty("id").GetGuid();

        var achieveResponse = await SendAchieveObjectiveAsync(_tenantA, subId);
        achieveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await achieveResponse.Content.ReadAsStringAsync());

        var editAfterAchieve = await SendEditObjectiveAsync(_tenantA, subId, "Should Not Apply", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 5m);
        editAfterAchieve.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an achieved milestone must be frozen for edits");

        var unachieveResponse = await SendUnachieveObjectiveAsync(_tenantA, subId);
        unachieveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AchieveObjective_WithUnachievedChild_Returns400()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Achieve Blocked Target", "ABT1");
        var defaultObjectiveId = (await ReadJsonAsync(created)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var parent = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Parent Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), 30m);
        var parentId = (await ReadJsonAsync(parent)).GetProperty("id").GetGuid();
        await SendCreateObjectiveAsync(_tenantA, parentId, "Unachieved Child", new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 1), 5m);

        var achieveResponse = await SendAchieveObjectiveAsync(_tenantA, parentId);

        achieveResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the child must be achieved before the parent can be");
    }

    [Fact]
    public async Task AchieveThenUnachieveProject_LeadManagesTopLevelState()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Achieve Project Target", "APT1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var achieveResponse = await SendAchieveProjectAsync(_tenantA, projectId);
        achieveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await achieveResponse.Content.ReadAsStringAsync());

        var unachieveResponse = await SendUnachieveProjectAsync(_tenantA, projectId);
        unachieveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetMyObjectiveHistory_NoInactiveMemberships_ReturnsEmptyArray()
    {
        var response = await _client.SendAsync(BuildGetRequest(_tenantA, "/api/v1/work/objectives/mine/history"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetArrayLength().Should().Be(0);
    }
```

- [ ] **Step 2: Add the shared HTTP helpers used above**

Add near the existing `SendCreateObjectiveAsync`/`SendEditObjectiveAsync`/`SendDeleteObjectiveAsync` helpers:

```csharp
    private async Task<HttpResponseMessage> SendAddObjectiveMemberAsync(TenantSession session, Guid objectiveId, Guid userId)
    {
        var body = new { userId };
        return await SendJsonAsync(HttpMethod.Post, session.Host, $"/api/v1/work/objectives/{objectiveId}/members", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    private async Task<HttpResponseMessage> SendRemoveObjectiveMemberAsync(TenantSession session, Guid objectiveId, Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/work/objectives/{objectiveId}/members/{userId}");
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAchieveObjectiveAsync(TenantSession session, Guid objectiveId)
        => await SendPostNoBodyAsync(session, $"/api/v1/work/objectives/{objectiveId}/achieve");

    private async Task<HttpResponseMessage> SendUnachieveObjectiveAsync(TenantSession session, Guid objectiveId)
        => await SendPostNoBodyAsync(session, $"/api/v1/work/objectives/{objectiveId}/unachieve");

    private async Task<HttpResponseMessage> SendAchieveProjectAsync(TenantSession session, Guid projectId)
        => await SendPostNoBodyAsync(session, $"/api/v1/work/projects/{projectId}/achieve");

    private async Task<HttpResponseMessage> SendUnachieveProjectAsync(TenantSession session, Guid projectId)
        => await SendPostNoBodyAsync(session, $"/api/v1/work/projects/{projectId}/unachieve");

    private async Task<HttpResponseMessage> SendPostNoBodyAsync(TenantSession session, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }
```

(`SendAddObjectiveMemberAsync` reuses the file's existing `SendJsonAsync` helper — do not redefine it.)

- [ ] **Step 3: Run the integration tests**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter CreateProjectEndpointTests`
Expected: all `[Fact]`s in the class pass — every pre-existing test from Foundation/Slice 2/Slice 3 plus this task's 6 new ones. Requires Docker running locally (Testcontainers), same precondition as the existing suite. This will take several minutes (the class has grown to ~30 tests) — run it in the background and check the result rather than waiting synchronously.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Features/WorkManagement/CreateProjectEndpointTests.cs
git commit -m "test(work-management): add HTTP integration tests for membership, member management, and Achieve"
```

---

### Task 17: `docs/postman-request/` docs for the 8 new endpoints + accuracy updates to 3 existing ones

**Files:**
- Create: `docs/postman-request/Work Management/Add Objective Member.md`
- Create: `docs/postman-request/Work Management/Remove Objective Member.md`
- Create: `docs/postman-request/Work Management/Achieve Objective.md`
- Create: `docs/postman-request/Work Management/Unachieve Objective.md`
- Create: `docs/postman-request/Work Management/Get Objective.md`
- Create: `docs/postman-request/Work Management/My Objective History.md`
- Create: `docs/postman-request/Work Management/Achieve Project.md`
- Create: `docs/postman-request/Work Management/Unachieve Project.md`
- Modify: `docs/postman-request/Work Management/Create Objective.md` — add a note that Create now also syncs project membership and auto-grants `projects:access` for the resolved Head.
- Modify: `docs/postman-request/Work Management/Edit Objective.md` — add `400` "milestone is achieved" to the Errors table.
- Modify: `docs/postman-request/Work Management/Transfer Objective Head.md` — add a note that applying a Transfer now also syncs membership and cascades `ReportingManagerId` to direct children, plus the `400` achieved-freeze error.

**Interfaces:**
- Consumes: nothing code-facing — required by `docs/superpowers/rules/PROCESS_RULES.md` rule 6, same format as every existing file in this folder (method+route, auth/permission/idempotency line, description, request/response examples, error table, Source section).

- [ ] **Step 1: `Add Objective Member.md`**

```markdown
# Add Objective Member

**POST** `/api/v1/work/objectives/{id}/members`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head.
**Idempotent:** Yes in effect — adding an already-active member is a no-op (204).

## Description

Adds a user to this milestone's project membership (`project_members`, scoped to this Objective's id). The user must be an active employee in this tenant. Does NOT grant `projects:access` — only assigning someone as Head does that (see Create/Transfer Objective Head).

## Request

```json
{ "userId": "guid" }
```

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | Milestone is achieved (frozen), or the user isn't an active employee in this tenant |
| `403` | Caller lacks `projects:access`, or is not this milestone's Head |
| `404` | Milestone doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`AddMember`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
```

- [ ] **Step 2: `Remove Objective Member.md`**

```markdown
# Remove Objective Member

**DELETE** `/api/v1/work/objectives/{id}/members/{userId}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head.

## Description

Deactivates a user's membership on this milestone. Removing the milestone's current Head is rejected — use Transfer Objective Head instead, which handles the membership handoff correctly.

## Request

No body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | Milestone is achieved (frozen), or `userId` is this milestone's current head |
| `403` | Caller lacks `projects:access`, or is not this milestone's Head |
| `404` | Milestone doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`RemoveMember`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
```

- [ ] **Step 3: `Achieve Objective.md`**

```markdown
# Achieve Objective

**POST** `/api/v1/work/objectives/{id}/achieve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head.
**Idempotent:** No — a second call on an already-achieved milestone returns `409`.

## Description

Marks a milestone Achieved (completion state, independent of soft-delete). Every direct sub-milestone must already be Achieved first. Same immediate-vs-pending split as Delete: applies immediately if the caller created this milestone, otherwise creates a pending `achieve` change request routed to the Reporting Manager. Once applied, the milestone is frozen (Edit/Transfer/member-management all return `400`) and the Head's active project participation is dropped unless they have another reason to stay (another milestone, or a direct membership) - see `GET /objectives/mine/history` for what happens to their access.

## Request

No body.

## Response

`204 No Content` (applied immediately) or `202 Accepted` with the created change request (pending approval).

## Errors

| Status | Cause |
|---|---|
| `400` | Target is the Default Objective (use the Project achieve endpoint), or a direct sub-milestone isn't yet Achieved |
| `403` | Caller lacks `projects:access`, or is not this milestone's Head |
| `404` | Milestone doesn't exist in tenant, or is inactive |
| `409` | Already achieved, or a change request is already pending for this milestone |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Achieve`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective/AchieveObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
```

- [ ] **Step 4: `Unachieve Objective.md`**

```markdown
# Unachieve Objective

**POST** `/api/v1/work/objectives/{id}/unachieve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head.

## Description

Reverts an Achieved milestone back to active, unfreezing it. No precondition (always reversible). Same immediate-vs-pending split as Achieve. On applying, restores the Head's active project membership.

## Request

No body.

## Response

`204 No Content` (applied immediately) or `202 Accepted` with the created change request (pending approval).

## Errors

| Status | Cause |
|---|---|
| `400` | Target is the Default Objective, or the current head is no longer an active employee in this tenant |
| `403` | Caller lacks `projects:access`, or is not this milestone's Head |
| `404` | Milestone doesn't exist in tenant, or is inactive |
| `409` | Milestone is not achieved, or a change request is already pending |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Unachieve`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UnachieveObjective/UnachieveObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
```

- [ ] **Step 5: `Get Objective.md`**

```markdown
# Get Objective

**GET** `/api/v1/work/objectives/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:read`/`*` OR an active membership on this milestone or any of its ancestors — checked in-handler, not via `[RequirePermission]` (same pattern as Get Project and Get Objective Tree).

## Description

Gets a single milestone by id.

## Response

`200 OK`

```json
{
  "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false,
  "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid|null",
  "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": "decimal|null",
  "allocatedHours": "decimal", "completedHours": "decimal", "isActive": true, "isAchieved": false,
  "achievedAt": "datetime|null", "createdAt": "datetime", "updatedAt": "datetime|null"
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller has neither `projects:read`/`*` nor an active membership on this milestone or an ancestor of it |
| `404` | Milestone doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetById`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
```

- [ ] **Step 6: `My Objective History.md`**

```markdown
# My Objective History

**GET** `/api/v1/work/objectives/mine/history`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`.

## Description

Milestones the caller used to have active access to (as Head or member) but no longer does - because they were Transferred away, removed as a member, or the milestone was Achieved and they had no other reason to stay in the project. Read-only; no write actions are available from this view.

## Response

`200 OK`

```json
[
  { "objectiveId": "guid", "title": "string", "projectId": "guid", "isAchieved": true, "removedAt": "datetime" }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`MyHistory`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyObjectiveHistory/GetMyObjectiveHistoryQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
```

- [ ] **Step 7: `Achieve Project.md`**

```markdown
# Achieve Project

**POST** `/api/v1/work/projects/{id}/achieve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be the Project's Lead.
**Idempotent:** No - a second call on an already-achieved project returns `409`.

## Description

Marks a Project Achieved. Every top-level milestone (direct child of the Default Objective) must already be Achieved first. Lead-only, always immediate - the Project is the tree's root, so there's no Reporting Manager to route an approval request to (same root exception as Edit/Delete Project).

## Request

No body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | A top-level milestone isn't yet Achieved |
| `403` | Caller lacks `projects:access`, or is not the project lead |
| `404` | Project doesn't exist in tenant |
| `409` | Project is already achieved |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`Achieve`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AchieveProject/AchieveProjectCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
```

- [ ] **Step 8: `Unachieve Project.md`**

```markdown
# Unachieve Project

**POST** `/api/v1/work/projects/{id}/unachieve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be the Project's Lead.

## Description

Reverts an Achieved project back to active. Lead-only, always immediate.

## Request

No body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or is not the project lead |
| `404` | Project doesn't exist in tenant |
| `409` | Project is not achieved |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`Unachieve`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/UnachieveProject/UnachieveProjectCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
```

- [ ] **Step 9: Update the 3 existing docs for accuracy**

In `Create Objective.md`, add to the Description section: *"Also syncs project membership for the resolved Head (creates or reactivates a `project_members` row scoped to the new milestone) and auto-grants `projects:access` if they don't already have it (takes effect on their next login - see design doc §7)."* Add `400` "assigned head must be an active employee in this tenant" to its Errors table.

In `Edit Objective.md`, add `400` "milestone is achieved" to its Errors table.

In `Transfer Objective Head.md`, add to the Description: *"Applying a transfer (immediately or via approval) also syncs project membership for both heads, cascades `ReportingManagerId` to the milestone's direct children, and drops the old head's project participation if they have no other active access."* Add `400` "milestone is achieved, or the new head isn't an active employee in this tenant" to its Errors table.

- [ ] **Step 10: Commit**

```bash
git add "docs/postman-request/Work Management/Add Objective Member.md" "docs/postman-request/Work Management/Remove Objective Member.md" "docs/postman-request/Work Management/Achieve Objective.md" "docs/postman-request/Work Management/Unachieve Objective.md" "docs/postman-request/Work Management/Get Objective.md" "docs/postman-request/Work Management/My Objective History.md" "docs/postman-request/Work Management/Achieve Project.md" "docs/postman-request/Work Management/Unachieve Project.md" "docs/postman-request/Work Management/Create Objective.md" "docs/postman-request/Work Management/Edit Objective.md" "docs/postman-request/Work Management/Transfer Objective Head.md"
git commit -m "docs(work-management): add postman-request docs for membership/member-management/Achieve endpoints"
```

---

## Self-review

**Spec coverage** (against `docs/superpowers/specs/2026-08-06-work-management-milestone-membership-and-achieve-design.md`):
- §2 Schema additions (IsAchieved/AchievedAt, achieve/unachieve request types) → Task 1.
- §3 Membership model (validation, Create/Transfer/member-add/remove sync, transactional) → Tasks 4, 6, 7, 8.
- §4 Dynamic Reporting Manager (cascade to direct children on Transfer, both immediate and approved paths) → Tasks 7, 11.
- §5 Scoped visibility (GetObjectiveTree subtree-scoping, new GetObjectiveById, new history endpoint) → Tasks 12, 13, 14.
- §6 Achieve (Objective + Project, precondition, frozen, revertible, membership cleanup) → Tasks 9, 10, 11.
- §7 Auto-grant projects:access + documented session-refresh limitation → Task 4, consumed by Tasks 6, 7.
- §8 Endpoint summary table (11 new/changed routes) → Task 15.

**Placeholder scan:** no "TBD"/"similar to Task N"/unshown code — every step has runnable code or an exact `dotnet`/`git` command.

**Type consistency:** `IMilestoneMembershipCoordinator`/`IPermissionAutoGrantService` signatures match exactly between Task 4's definition and every consuming handler in Tasks 6–14; `ObjectiveChangeOutcomeResponse` (defined by the original milestone-hierarchy plan, reused here) is constructed identically in Tasks 9, 10, 11; `EmploymentStatusIds.Active` and `ObjectiveChangeRequestTypes.Achieve`/`.Unachieve` (Task 1) are referenced with matching names throughout.

**Known, accepted deviation from strict minimalism:** `IUnitOfWork.ExecuteInTransactionAsync` wraps several handlers (Tasks 6, 7, 9, 11) even though a single `SaveChangesAsync` call against one `DbContext` is already atomic for everything staged in this plan — no raw SQL or multiple separate `SaveChangesAsync` calls occur in any of them, which is the actual scenario that helper exists for. This is redundant, not incorrect, and is called out here explicitly rather than silently shipped as if it were necessary - worth a simplification pass during implementation review if a reviewer flags it, but not blocking.
