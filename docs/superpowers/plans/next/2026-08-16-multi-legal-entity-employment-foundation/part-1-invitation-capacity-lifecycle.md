# Invitation Capacity Reservation & Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the race where two invitations can be issued against the last vacant seat of a position, and add the ability to revoke a pending employee invitation (freeing its reserved seat).

**Architecture:** `PositionAssignment.AssignmentStatus` already has an unused `"planned"` value alongside `"active"`. Today, invite creation checks capacity by counting only `active` assignments, then separately inserts nothing for the pending invite — so a second concurrent invite for the same seat can pass the same check before either commits. This plan makes seat reservation atomic (one SQL statement, mirroring the existing `EfTenantStorageStatsRepository.TryReserveBytesAsync` pattern in this codebase) by inserting a `planned` `PositionAssignment` row *guarded by a capacity subquery in the same INSERT statement*, at invite-creation time. The reserved row flips to `active` on accept, or is cancelled on revoke.

**Tech Stack:** .NET (C#), EF Core (PostgreSQL, raw parameterized SQL via `ExecuteSqlInterpolatedAsync` for the atomic reserve), MediatR (CQRS commands/queries), xUnit + Moq (unit tests), Testcontainers (integration tests).

## Global Constraints

- Snake_case DB column naming (EF Core convention already configured project-wide) — e.g. `PositionAssignment.EmployeeId` maps to `employee_id`.
- Every command handler that mutates data saves through the same `SaveChangesAsync`/`IUnitOfWork` pattern already used in the handler being modified — do not introduce a second, separate `SaveChangesAsync` call.
- New permission codes are added to `PermissionSeeder.cs` only — never hand-inserted via a migration data-seed (this repo seeds permissions in code, not SQL).
- `InvitationValidityHours = 24` is already the value in both `ApproveAccessGrantRequestCommandHandler` and `ResendEmployeeInvitationCommandHandler` (confirmed in code) — do not change it.
- Test file locations mirror source structure: unit tests under `tests/ONEVO.Tests.Unit/Features/...`, integration under `tests/ONEVO.Tests.Integration/...`.

---

### Task 1: Add `invitations:manage` permission and re-point the Resend endpoint to it

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs:74`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/Permission/PermissionSeederTests.cs` (create if it doesn't already exist — check first)

**Interfaces:**
- Produces: permission code `"invitations:manage"`, seeded with module `"core_hr"` (same module as the `employees:*` permissions it sits beside).

- [ ] **Step 1: Find the exact insertion point in `PermissionSeeder.cs`**

Open `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs` and find these two existing lines (already confirmed present):

```csharp
Perm("employees:read", "View all employees in scope.", "core_hr"),
Perm("employees:write", "Create, update employees.", "core_hr"),
```

- [ ] **Step 2: Add the new permission immediately after them**

```csharp
Perm("employees:read", "View all employees in scope.", "core_hr"),
Perm("employees:write", "Create, update employees.", "core_hr"),
Perm("invitations:manage", "Resend or revoke employee onboarding invitations.", "core_hr"),
```

- [ ] **Step 3: Check for an existing seeder idempotency test and extend it, or write one**

Search `tests/ONEVO.Tests.Unit` for a test asserting the full seeded permission list (e.g. a test named `*PermissionSeeder*Tests.cs`). If one exists, add `"invitations:manage"` to its expected-codes assertion. If none exists, create `tests/ONEVO.Tests.Unit/Features/Auth/Permission/PermissionSeederTests.cs`:

```csharp
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Permission;

public class PermissionSeederTests
{
    [Fact]
    public void SeedData_ContainsInvitationsManage_WithCoreHrModule()
    {
        var permissions = PermissionSeeder.GetSeedData(); // adjust to the actual static accessor name found in Step 1

        var invitationsManage = Assert.Single(permissions, p => p.Code == "invitations:manage");
        Assert.Equal("core_hr", invitationsManage.Module);
    }
}
```

If `PermissionSeeder`'s seed list isn't exposed via a static method (check the class — it may only run inside a `SeedAsync(DbContext)` instance method), skip writing a new isolated test and instead confirm the permission appears by running the full seeder integration test suite in Task 6 of this file.

- [ ] **Step 4: Re-point the Resend endpoint's permission attribute**

In `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`, change:

```csharp
[HttpPost("{id:guid}/resend-invitation")]
[RequirePermission("employees:write")]
public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken ct = default)
```

to:

```csharp
[HttpPost("{id:guid}/resend-invitation")]
[RequirePermission("invitations:manage")]
public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken ct = default)
```

- [ ] **Step 5: Run the unit test suite for the touched areas**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PermissionSeeder|FullyQualifiedName~ResendEmployeeInvitation"`
Expected: PASS (existing `ResendEmployeeInvitationCommandHandlerTests` don't assert on the controller attribute, so they should be unaffected; if any integration test asserts the old permission on this route, it will need updating — search `tests/ONEVO.Tests.Integration` for `resend-invitation` and update any `employees:write` expectation there to `invitations:manage`).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/Auth/Permission/PermissionSeederTests.cs
git commit -m "feat: add invitations:manage permission, gate resend-invitation on it"
```

---

### Task 2: Add `PositionAssignmentId` to `InvitationToken` (migration)

**Files:**
- Modify: `src/ONEVO.Domain/Features/Auth/Invite/Entities/InvitationToken.cs`
- Create: EF Core migration (generated file under `src/ONEVO.Infrastructure/Migrations/`)

**Interfaces:**
- Produces: `InvitationToken.PositionAssignmentId` (`Guid?`) — the exact reserved-seat row this invitation corresponds to, so accept/revoke/resend can reference it directly instead of re-deriving it from `EmployeeId`+`PositionId`.

- [ ] **Step 1: Add the property**

In `src/ONEVO.Domain/Features/Auth/Invite/Entities/InvitationToken.cs`, add this line directly under the existing `EmployeeId` property:

```csharp
    public Guid? EmployeeId { get; set; }
    public Guid? PositionAssignmentId { get; set; }
    public Guid? OnboardingDraftId { get; set; }
```

- [ ] **Step 2: Generate the migration**

Run:
```bash
dotnet ef migrations add AddInvitationTokenPositionAssignmentId --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

- [ ] **Step 3: Inspect the generated migration file**

Open the newly generated file under `src/ONEVO.Infrastructure/Migrations/`. Confirm it contains exactly one `AddColumn` call for `PositionAssignmentId` (nullable uuid) on the `invitation_tokens` table, and nothing else. If EF Core picked up unrelated pending model changes, stop and investigate before proceeding (do not silently include unrelated schema drift in this migration).

- [ ] **Step 4: Apply the migration locally and verify**

Run:
```bash
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Expected: succeeds with no errors; `invitation_tokens` now has a `position_assignment_id` nullable uuid column.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/Auth/Invite/Entities/InvitationToken.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add InvitationToken.PositionAssignmentId column"
```

---

### Task 3: Atomic seat reservation on `IPositionAssignmentRepository`

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/TryReservePositionAssignmentTests.cs` (create)

**Interfaces:**
- Produces: `Task<Guid?> TryReservePositionAssignmentAsync(Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById, CancellationToken ct = default)` — returns the new `PositionAssignment.Id` (status `"planned"`) if a seat was available and reserved, or `null` if the position was already at capacity (counting both `active` and `planned` occupants). Single atomic SQL statement — no separate count-then-insert round trip, so no race window exists between two concurrent callers.

This mirrors the existing `EfTenantStorageStatsRepository.TryReserveBytesAsync` pattern already used in this codebase (`src/ONEVO.Infrastructure/Persistence/Repositories/Storage/Quota/EfTenantStorageStatsRepository.cs`): a single `INSERT ... WHERE <capacity condition>` statement, success determined by whether any row was affected.

- [ ] **Step 1: Write the failing integration test**

Create `tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/TryReservePositionAssignmentTests.cs`. Follow this repo's existing Testcontainers integration-test base class pattern (find and inherit the same base class another integration test under `tests/ONEVO.Tests.Integration/CoreHr/` uses — e.g. look at how `FinalizeOnboardingDraftCommandHandlerTests`' integration counterpart, if any, or any other `EmployeesListIntegrationTests`-style test, sets up its tenant/DbContext/seed data, and copy that exact setup shape):

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.PositionAssignment;

public class TryReservePositionAssignmentTests : IntegrationTestBase // adjust to the real base class name found above
{
    [Fact]
    public async Task TryReserve_WhenSeatAvailable_InsertsPlannedRowAndReturnsId()
    {
        var tenantId = await SeedTenantAsync();
        var (employeeId, positionId) = await SeedEmployeeAndUniquePositionAsync(tenantId); // position_type='unique', max_occupancy=1, no existing assignments

        var repo = new EfPositionAssignmentRepository(Db);
        var reservedId = await repo.TryReservePositionAssignmentAsync(
            tenantId, employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());

        Assert.NotNull(reservedId);

        var row = await Db.PositionAssignments.FindAsync(reservedId!.Value);
        Assert.NotNull(row);
        Assert.Equal("planned", row!.AssignmentStatus);
    }

    [Fact]
    public async Task TryReserve_WhenPositionAlreadyAtCapacityFromAnotherPlannedRow_ReturnsNullAndInsertsNothing()
    {
        var tenantId = await SeedTenantAsync();
        var (employeeId, positionId) = await SeedEmployeeAndUniquePositionAsync(tenantId);
        var (otherEmployeeId, _) = await SeedEmployeeAndUniquePositionAsync(tenantId, existingPositionId: positionId);

        var repo = new EfPositionAssignmentRepository(Db);
        var first = await repo.TryReservePositionAssignmentAsync(
            tenantId, employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());
        Assert.NotNull(first);

        var second = await repo.TryReservePositionAssignmentAsync(
            tenantId, otherEmployeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());

        Assert.Null(second);
        var count = await Db.PositionAssignments.CountAsync(pa => pa.PositionId == positionId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task TryReserve_TwoConcurrentCallsAgainstOneVacancy_ExactlyOneSucceeds()
    {
        var tenantId = await SeedTenantAsync();
        var (employeeA, positionId) = await SeedEmployeeAndUniquePositionAsync(tenantId);
        var (employeeB, _) = await SeedEmployeeAndUniquePositionAsync(tenantId, existingPositionId: positionId);

        var repoA = new EfPositionAssignmentRepository(CreateNewDbContext()); // separate DbContext/connection per concurrent caller
        var repoB = new EfPositionAssignmentRepository(CreateNewDbContext());

        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var taskA = repoA.TryReservePositionAssignmentAsync(tenantId, employeeA, positionId, effectiveFrom, Guid.NewGuid());
        var taskB = repoB.TryReservePositionAssignmentAsync(tenantId, employeeB, positionId, effectiveFrom, Guid.NewGuid());
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, r => r is not null);
        Assert.Single(results, r => r is null);
    }
}
```

Adjust `SeedTenantAsync`/`SeedEmployeeAndUniquePositionAsync`/`CreateNewDbContext` to whatever this repo's existing integration test helpers are actually named — find them by reading one existing integration test file under `tests/ONEVO.Tests.Integration/CoreHr/` before writing this file, since guessing the helper names would break the build.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~TryReservePositionAssignment"`
Expected: FAIL (build error — `TryReservePositionAssignmentAsync` doesn't exist yet)

- [ ] **Step 3: Add the interface method**

In `src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs`, add:

```csharp
    /// <summary>Atomically reserves a seat for the given position by inserting a "planned"
    /// PositionAssignment row, guarded by a capacity subquery in the same SQL statement (counts
    /// both active and planned occupants against Position.MaxOccupancy). Returns the new row's
    /// Id on success, or null if the position was already at capacity - no separate count-then-
    /// insert round trip, so two concurrent callers targeting the last vacancy cannot both
    /// succeed.</summary>
    Task<Guid?> TryReservePositionAssignmentAsync(
        Guid tenantId,
        Guid employeeId,
        Guid positionId,
        DateOnly effectiveFrom,
        Guid createdById,
        CancellationToken ct = default);

    /// <summary>Flips a "planned" PositionAssignment row to "active" (on invite accept). No-op
    /// (returns false) if the row doesn't exist or isn't currently "planned".</summary>
    Task<bool> ActivatePlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default);

    /// <summary>Flips a "planned" PositionAssignment row to "cancelled" (on invite revoke),
    /// freeing the seat. No-op (returns false) if the row doesn't exist or isn't currently
    /// "planned".</summary>
    Task<bool> CancelPlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default);
```

- [ ] **Step 4: Implement all three methods**

In `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs`, add:

```csharp
    public async Task<Guid?> TryReservePositionAssignmentAsync(
        Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
        CancellationToken ct = default)
    {
        var newId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO position_assignments
                (id, tenant_id, employee_id, position_id, assignment_kind, effective_from,
                 assignment_status, created_by_id, created_at, is_deleted)
            SELECT {newId}, {tenantId}, {employeeId}, {positionId}, {PositionAssignmentKind.PrimaryEmployment},
                   {effectiveFrom}, {PositionAssignmentStatus.Planned}, {createdById}, {now}, false
            WHERE (
                SELECT COUNT(*) FROM position_assignments
                WHERE tenant_id = {tenantId} AND position_id = {positionId}
                  AND assignment_kind = {PositionAssignmentKind.PrimaryEmployment}
                  AND assignment_status IN ({PositionAssignmentStatus.Active}, {PositionAssignmentStatus.Planned})
            ) < (
                SELECT max_occupancy FROM positions WHERE id = {positionId} AND tenant_id = {tenantId}
            )
        ", ct);

        return rowsAffected > 0 ? newId : null;
    }

    public async Task<bool> ActivatePlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE position_assignments
            SET assignment_status = {PositionAssignmentStatus.Active}, updated_at = {now}
            WHERE id = {positionAssignmentId} AND tenant_id = {tenantId}
              AND assignment_status = {PositionAssignmentStatus.Planned}
        ", ct);
        return rowsAffected > 0;
    }

    public async Task<bool> CancelPlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE position_assignments
            SET assignment_status = {PositionAssignmentStatus.Cancelled}, updated_at = {now}
            WHERE id = {positionAssignmentId} AND tenant_id = {tenantId}
              AND assignment_status = {PositionAssignmentStatus.Planned}
        ", ct);
        return rowsAffected > 0;
    }
```

Add `using ONEVO.Domain.Features.CoreHr.Entities;` at the top of the file if not already present (needed for `PositionAssignmentKind`/`PositionAssignmentStatus`).

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~TryReservePositionAssignment"`
Expected: PASS (all 3 tests)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/TryReservePositionAssignmentTests.cs
git commit -m "feat: add atomic seat reservation to IPositionAssignmentRepository"
```

---

### Task 4: Wire reservation into `ApproveAccessGrantRequestCommandHandler`

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ApproveAccessGrantRequest/ApproveAccessGrantRequestCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.TryReservePositionAssignmentAsync(...)` from Task 3.

- [ ] **Step 1: Write the failing unit test**

Add to `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs` (find the existing test class's constructor/mock-setup pattern and match it exactly — every existing test in this file already mocks `IPositionAssignmentRepository`, so this is an additive test, not a new fixture):

```csharp
    [Fact]
    public async Task Handle_WhenPositionAtCapacity_ReturnsConflict_AndDoesNotCreateEmployee()
    {
        // Arrange: reuse this file's existing "happy path" mock setup, but make
        // TryReservePositionAssignmentAsync return null (capacity full) instead of a Guid.
        _positionAssignmentRepository
            .Setup(r => r.TryReservePositionAssignmentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await _handler.Handle(BuildValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("This position has reached its capacity.", result.Error);
        _employeeRepository.Verify(r => r.AddAsync(It.IsAny<ONEVO.Domain.Features.CoreHr.Entities.Employee>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Adjust `BuildValidCommand()`/mock field names (`_positionAssignmentRepository`, `_employeeRepository`, `_handler`) to whatever this existing test file actually calls them — read the file first.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ApproveAccessGrantRequestCommandHandlerTests"`
Expected: FAIL (the handler still uses `CountActiveAsync`, so `TryReservePositionAssignmentAsync` is never called and the mock setup is irrelevant — the old code path returns success instead of the expected 409)

- [ ] **Step 3: Replace the capacity check + assignment creation**

In `ApproveAccessGrantRequestCommandHandler.cs`, replace this block:

```csharp
        // Capacity, same signal FinalizeOnboardingDraftCommandHandler uses.
        var activeAssignmentCount = await _positionAssignmentRepository.CountActiveAsync(tenantId, position.Id, ct);
        if (activeAssignmentCount >= position.MaxOccupancy)
            return Result<ApproveAccessGrantRequestResponse>.Conflict("This position has reached its capacity.");
```

with a no-op placeholder removal (delete it entirely — the reservation now happens later, once `employeeId` is known, replacing the separate `AddAsync` call). Then find this later block:

```csharp
        var assignment = new PositionAssignmentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = draft.TenantId,
            EmployeeId = employeeId,
            PositionId = position.Id,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            EffectiveFrom = draft.StartDate,
            AssignmentStatus = PositionAssignmentStatus.Active,
            CreatedById = _currentUser.UserId,
        };
        await _positionAssignmentRepository.AddAsync(assignment, ct);
```

and replace it with:

```csharp
        var reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
            draft.TenantId, employeeId, position.Id, draft.StartDate, _currentUser.UserId, ct);
        if (reservedAssignmentId is null)
            return Result<ApproveAccessGrantRequestResponse>.Conflict("This position has reached its capacity.");
```

This moves the capacity check to immediately before the reservation succeeds/fails (right after `employeeId` is minted, since the reservation needs it), rather than as an early separate check — the atomic reserve-or-fail call *is* the capacity check now, so the old two-step check-then-insert is gone entirely. Note: `PositionAssignmentEntity` may now be unused elsewhere in this file — if so, remove its `using`/type-alias only if no other reference remains (search the file for `PositionAssignmentEntity` before removing the alias at the top).

- [ ] **Step 4: Carry `reservedAssignmentId` into the invitation**

Find:

```csharp
        var invitation = new InvitationToken
        {
            Id = Guid.NewGuid(),
            TenantId = draft.TenantId,
            UserId = user.Id,
            RoleId = null,
            PositionId = draft.PositionId,
```

and add the new field:

```csharp
        var invitation = new InvitationToken
        {
            Id = Guid.NewGuid(),
            TenantId = draft.TenantId,
            UserId = user.Id,
            RoleId = null,
            PositionId = draft.PositionId,
            PositionAssignmentId = reservedAssignmentId,
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ApproveAccessGrantRequestCommandHandlerTests"`
Expected: PASS (all tests in this file, including the new one and every pre-existing one — update any pre-existing test's mock setup from `CountActiveAsync`/`AddAsync` to `TryReservePositionAssignmentAsync` returning a non-null Guid, or those tests will now fail since the old methods are no longer called)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ApproveAccessGrantRequest/ApproveAccessGrantRequestCommandHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs
git commit -m "feat: reserve seat atomically in ApproveAccessGrantRequestCommandHandler"
```

---

### Task 5: Wire reservation into `FinalizeOnboardingDraftCommandHandler`'s immediate-finalize branch

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs`

**Interfaces:**
- Consumes: same `TryReservePositionAssignmentAsync` from Task 3.

- [ ] **Step 1: Read the handler's immediate-finalize branch first**

Open `FinalizeOnboardingDraftCommandHandler.cs` and locate the block starting around the confirmed line `var normalizedEmail = draft.WorkEmail.Trim().ToLowerInvariant();` (line ~293) through wherever it constructs a `PositionAssignmentEntity` with `AssignmentStatus = PositionAssignmentStatus.Active` and calls `_positionAssignmentRepository.AddAsync(...)`. This branch is structurally identical to `ApproveAccessGrantRequestCommandHandler`'s equivalent block (Task 4, Step 3) — same field names, same pattern, just reached via a different code path (immediate finalize, not deferred-then-approved).

- [ ] **Step 2: Write the failing unit test**

Mirror Task 4 Step 1 exactly, but against this handler's test class and its own mock-setup helper/constructor pattern (read the file first to match names):

```csharp
    [Fact]
    public async Task Handle_ImmediateFinalize_WhenPositionAtCapacity_ReturnsConflict()
    {
        _positionAssignmentRepository
            .Setup(r => r.TryReservePositionAssignmentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await _handler.Handle(BuildImmediateFinalizeCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("This position has reached its capacity.", result.Error);
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~FinalizeOnboardingDraftCommandHandlerTests"`
Expected: FAIL

- [ ] **Step 4: Apply the same two edits as Task 4, Steps 3–4, to this handler**

1. Remove the early `CountActiveAsync`/`MaxOccupancy` check block (confirmed at lines ~208-213: `if (position is not null) { var activeAssignmentCount = ...; if (activeAssignmentCount >= position.MaxOccupancy) return Conflict(...); }`) — but note this handler's early check runs **before** the `requiresApproval` branch splits (it gates both the deferred and immediate paths). Since the deferred path creates nothing until `ApproveAccessGrantRequestCommandHandler` runs (which now does its own atomic reserve, Task 4), this early check is safe to remove entirely for both branches — the deferred branch never reserved a seat here anyway, and the immediate branch will now reserve one atomically at the point below.
2. In the immediate-finalize branch (Step 1's location), replace the `PositionAssignmentEntity`/`AddAsync` block with the same `TryReservePositionAssignmentAsync` call + null-check pattern as Task 4 Step 3, and thread `reservedAssignmentId` into this handler's own `InvitationToken` construction (`PositionAssignmentId = reservedAssignmentId`) the same way as Task 4 Step 4.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~FinalizeOnboardingDraftCommandHandlerTests"`
Expected: PASS (all tests — update pre-existing mock setups the same way as Task 4 Step 5)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs
git commit -m "feat: reserve seat atomically in FinalizeOnboardingDraftCommandHandler"
```

---

### Task 6: Flip reserved seat to active on accept

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptEmployeeInvitation/AcceptEmployeeInvitationCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/Invite/AcceptEmployeeInvitationCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.ActivatePlannedAsync(tenantId, positionAssignmentId, ct)` from Task 3.

- [ ] **Step 1: Write the failing unit test**

Add to `AcceptEmployeeInvitationCommandHandlerTests.cs` (match the existing file's mock/constructor pattern):

```csharp
    [Fact]
    public async Task Handle_SuccessfulAccept_ActivatesReservedPositionAssignment()
    {
        var positionAssignmentId = Guid.NewGuid();
        var invitation = BuildValidPendingInvitation() with { PositionAssignmentId = positionAssignmentId }; // adjust to this file's existing invitation-builder helper; InvitationToken is a class not a record if `with` doesn't compile, in which case set the property directly on the built instance
        _invitations.Setup(i => i.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

        await _handler.Handle(BuildValidAcceptCommand(), CancellationToken.None);

        _positionAssignmentRepository.Verify(
            r => r.ActivatePlannedAsync(invitation.TenantId, positionAssignmentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AcceptEmployeeInvitationCommandHandlerTests"`
Expected: FAIL (constructor doesn't accept `IPositionAssignmentRepository` yet, or the mock is simply never called — build error if you add the mock field before the constructor is updated, so update the constructor in Step 3 first if the test file won't compile)

- [ ] **Step 3: Add the dependency and the activation call**

In `AcceptEmployeeInvitationCommandHandler.cs`, add the field, constructor parameter, and assignment (mirroring every other dependency already in this class):

```csharp
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
```

```csharp
    public AcceptEmployeeInvitationCommandHandler(
        IInvitationTokenRepository invitations,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ILegalAcceptanceSubmissionService legalSubmission,
        ILoginContinuationService continuation,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        ITenantContext tenantContext,
        IGlobalEmailDirectoryRepository globalDirectory,
        IPositionAssignmentRepository positionAssignmentRepository)
    {
        _invitations = invitations;
        _users = users;
        _passwordHasher = passwordHasher;
        _legalSubmission = legalSubmission;
        _continuation = continuation;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _tenantContext = tenantContext;
        _globalDirectory = globalDirectory;
        _positionAssignmentRepository = positionAssignmentRepository;
    }
```

Then, immediately after the existing lines:

```csharp
        inv.UsedAt = now;
        inv.CompletedWith = "employee_password";
```

add:

```csharp
        if (inv.PositionAssignmentId is Guid reservedAssignmentId)
            await _positionAssignmentRepository.ActivatePlannedAsync(inv.TenantId, reservedAssignmentId, ct);
```

Add `using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;` at the top of the file.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AcceptEmployeeInvitationCommandHandlerTests"`
Expected: PASS (all tests — every other existing test in this file needs the new constructor parameter added to its handler instantiation, or the file won't compile; use `Mock.Of<IPositionAssignmentRepository>()` or a shared mock field for tests that don't care about this behavior)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptEmployeeInvitation/AcceptEmployeeInvitationCommandHandler.cs tests/ONEVO.Tests.Unit/Features/Auth/Invite/AcceptEmployeeInvitationCommandHandlerTests.cs
git commit -m "feat: activate the reserved seat on employee invitation accept"
```

---

### Task 7: Carry `PositionAssignmentId` forward on resend

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ResendEmployeeInvitation/ResendEmployeeInvitationCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ResendEmployeeInvitationCommandHandlerTests.cs`

**Interfaces:**
- No new dependency — this is a one-field addition to an object this handler already constructs.

- [ ] **Step 1: Write the failing unit test**

Add to `ResendEmployeeInvitationCommandHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Handle_Resend_CarriesForwardPositionAssignmentId()
    {
        var reservedAssignmentId = Guid.NewGuid();
        var expiredInvitation = BuildExpiredInvitation() with { PositionAssignmentId = reservedAssignmentId }; // adjust to this file's existing builder helper name; set the property directly if InvitationToken is a class

        InvitationToken? captured = null;
        _invitationTokenRepository
            .Setup(r => r.AddAsync(It.IsAny<InvitationToken>(), It.IsAny<CancellationToken>()))
            .Callback<InvitationToken, CancellationToken>((inv, _) => captured = inv)
            .Returns(Task.CompletedTask);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredInvitation);

        await _handler.Handle(new ResendEmployeeInvitationCommand(expiredInvitation.EmployeeId!.Value), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(reservedAssignmentId, captured!.PositionAssignmentId);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ResendEmployeeInvitationCommandHandlerTests"`
Expected: FAIL (`captured.PositionAssignmentId` is null — never set)

- [ ] **Step 3: Add the field to the constructed invitation**

Find:

```csharp
        var invitation = new InvitationToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = current.UserId,
            RoleId = null,
            PositionId = current.PositionId,
```

and add:

```csharp
        var invitation = new InvitationToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = current.UserId,
            RoleId = null,
            PositionId = current.PositionId,
            PositionAssignmentId = current.PositionAssignmentId,
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ResendEmployeeInvitationCommandHandlerTests"`
Expected: PASS (all tests)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Commands/ResendEmployeeInvitation/ResendEmployeeInvitationCommandHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ResendEmployeeInvitationCommandHandlerTests.cs
git commit -m "feat: carry reserved seat forward when resending an invitation"
```

---

### Task 8: Revoke invitation command + endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/RevokeEmployeeInvitation/RevokeEmployeeInvitationCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/RevokeEmployeeInvitation/RevokeEmployeeInvitationCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/RevokeEmployeeInvitation/RevokeEmployeeInvitationCommandValidator.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/RevokeEmployeeInvitationCommandHandlerTests.cs` (create)

**Interfaces:**
- Produces: `RevokeEmployeeInvitationCommand(Guid EmployeeId)`, `POST /api/v1/employees/{id}/revoke-invitation`, gated `[RequirePermission("invitations:manage")]`.
- Consumes: `IPositionAssignmentRepository.CancelPlannedAsync(...)` from Task 3.

- [ ] **Step 1: Write the failing unit test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/RevokeEmployeeInvitationCommandHandlerTests.cs`, following `ResendEmployeeInvitationCommandHandlerTests.cs`'s exact mock/constructor setup pattern (same dependencies: `IEmployeeRepository`, `IInvitationTokenRepository`, `IUnitOfWork`, `ICurrentUser`, `IDateTimeProvider`, plus the new `IPositionAssignmentRepository`):

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using Xunit;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class RevokeEmployeeInvitationCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IInvitationTokenRepository> _invitationTokenRepository = new();
    private readonly Mock<IPositionAssignmentRepository> _positionAssignmentRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private RevokeEmployeeInvitationCommandHandler CreateHandler() => new(
        _employeeRepository.Object,
        _invitationTokenRepository.Object,
        _positionAssignmentRepository.Object,
        _unitOfWork.Object,
        _currentUser.Object,
        _clock.Object);

    private static InvitationToken BuildPendingInvitation(Guid tenantId, Guid employeeId, Guid? positionAssignmentId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        PositionAssignmentId = positionAssignmentId,
        InvitedEmail = "person@example.com",
        InvitedFullName = "Person Example",
        Status = "pending",
        TokenHash = "hash",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Handle_PendingInvitation_RevokesTokenAndCancelsReservedSeat()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionAssignmentId = Guid.NewGuid();
        var invitation = BuildPendingInvitation(tenantId, employeeId, positionAssignmentId);

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokeEmployeeInvitationCommand(employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(invitation.RevokedAt);
        _positionAssignmentRepository.Verify(
            r => r.CancelPlannedAsync(tenantId, positionAssignmentId, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyAcceptedInvitation_ReturnsFailure_DoesNotRevoke()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invitation = BuildPendingInvitation(tenantId, employeeId, Guid.NewGuid());
        invitation.UsedAt = DateTimeOffset.UtcNow.AddHours(-1);

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokeEmployeeInvitationCommand(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("This invitation has already been accepted.", result.Error);
        _positionAssignmentRepository.Verify(
            r => r.CancelPlannedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyRevokedInvitation_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invitation = BuildPendingInvitation(tenantId, employeeId, Guid.NewGuid());
        invitation.RevokedAt = DateTimeOffset.UtcNow.AddHours(-1);

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokeEmployeeInvitationCommand(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("This invitation has already been revoked.", result.Error);
    }

    [Fact]
    public async Task Handle_NoInvitationFound_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokeEmployeeInvitationCommand(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("No invitation has been sent to this employee.", result.Error);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~RevokeEmployeeInvitationCommandHandlerTests"`
Expected: FAIL (build error — `RevokeEmployeeInvitationCommand`/`RevokeEmployeeInvitationCommandHandler` don't exist yet)

- [ ] **Step 3: Create the command**

`src/ONEVO.Application/Features/CoreHr/Employee/Commands/RevokeEmployeeInvitation/RevokeEmployeeInvitationCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;

public sealed record RevokeEmployeeInvitationCommand(Guid EmployeeId) : IRequest<Result<Unit>>;
```

(If this codebase's `Result<T>` doesn't support `Result<Unit>` cleanly, check `ResendEmployeeInvitationCommand`'s return-type pattern and use `Result<RevokeEmployeeInvitationResponse>` with an empty response record instead — match whatever convention `ResendEmployeeInvitationCommand`/`Response` already establishes.)

- [ ] **Step 4: Create the validator**

`src/ONEVO.Application/Features/CoreHr/Employee/Commands/RevokeEmployeeInvitation/RevokeEmployeeInvitationCommandValidator.cs`:

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;

public sealed class RevokeEmployeeInvitationCommandValidator : AbstractValidator<RevokeEmployeeInvitationCommand>
{
    public RevokeEmployeeInvitationCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}
```

- [ ] **Step 5: Create the handler**

`src/ONEVO.Application/Features/CoreHr/Employee/Commands/RevokeEmployeeInvitation/RevokeEmployeeInvitationCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;

/// <summary>
/// HR-triggered revoke from the employee detail screen (pairs with ResendEmployeeInvitation).
/// Marks the current invitation token revoked and cancels its reserved seat (if one exists),
/// freeing the position's capacity for a different candidate. Unlike Resend, this works on a
/// still-pending invitation too - revoke is the explicit "stop, don't let this person in" action,
/// not limited to expired tokens.
/// </summary>
public sealed class RevokeEmployeeInvitationCommandHandler
    : IRequestHandler<RevokeEmployeeInvitationCommand, Result<Unit>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RevokeEmployeeInvitationCommandHandler(
        IEmployeeRepository employeeRepository,
        IInvitationTokenRepository invitationTokenRepository,
        IPositionAssignmentRepository positionAssignmentRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _employeeRepository = employeeRepository;
        _invitationTokenRepository = invitationTokenRepository;
        _positionAssignmentRepository = positionAssignmentRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<Unit>> Handle(RevokeEmployeeInvitationCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var current = await _invitationTokenRepository.GetLatestByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (current is null)
            return Result<Unit>.Failure("No invitation has been sent to this employee.", 400);

        if (current.UsedAt is not null)
            return Result<Unit>.Failure("This invitation has already been accepted.", 400);
        if (current.RevokedAt is not null)
            return Result<Unit>.Failure("This invitation has already been revoked.", 400);

        current.RevokedAt = _clock.UtcNow;
        current.RevokedById = _currentUser.UserId;

        if (current.PositionAssignmentId is Guid reservedAssignmentId)
            await _positionAssignmentRepository.CancelPlannedAsync(tenantId, reservedAssignmentId, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}
```

- [ ] **Step 6: Wire the endpoint**

In `EmployeesController.cs`, add directly after the existing `ResendInvitation` action:

```csharp
    /// <summary>Revoke an employee's current onboarding invitation and free its reserved seat.
    /// Unlike resend, this works on a still-pending invitation, not only an expired one.</summary>
    [HttpPost("{id:guid}/revoke-invitation")]
    [RequirePermission("invitations:manage")]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RevokeEmployeeInvitationCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;` to the controller's usings.

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~RevokeEmployeeInvitationCommandHandlerTests"`
Expected: PASS (all 4 tests)

- [ ] **Step 8: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS (every test — this confirms Tasks 1-8 together haven't broken anything else in the invitation/onboarding/employee area)

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Commands/RevokeEmployeeInvitation/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/RevokeEmployeeInvitationCommandHandlerTests.cs
git commit -m "feat: add revoke-invitation endpoint, frees the reserved seat"
```

---

## Part 1 done — what's next

Part 2 (`part-2-cross-legal-entity-invitation.md`) builds on this: the atomic seat reservation from Task 3 is reused unchanged when a cross-legal-entity invitation reserves a seat in the *target* entity's position.
