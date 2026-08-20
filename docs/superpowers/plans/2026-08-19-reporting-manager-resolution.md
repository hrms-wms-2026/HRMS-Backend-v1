# Reporting Manager Resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make an employee's manager resolve correctly everywhere the app needs it — including when their position's report-to target has multiple simultaneous holders — by fixing the dead `EmployeeHierarchyClosure.RebuildAsync` mechanism and adding a narrow `PositionAssignment.ReportsToEmployeeId` override that's asked for only when the target position is ambiguous, across single-employee onboarding, bulk onboarding, and Change Position.

**Architecture:** Backend: one new nullable column on `position_assignments` (no FK — "current active holder" is time-varying, so it's validated at write time, not DB-constrained) and a mirrored nullable column on `onboarding_drafts` (Save and Finalize are separate requests, so the value must persist between them). `EfEmployeeHierarchyClosureRepository.RebuildAsync` is fixed to resolve unique targets automatically and pooled targets only via the override, then wired into every write path that changes an active `PrimaryEmployment` assignment or a `ReportsToPositionId`. Frontend: a new shared "person picker" component consumed by the onboarding wizard and the Change Position modal, plus a new conditionally-required bulk-onboarding CSV field.

**Tech Stack:** .NET 10 / EF Core (Npgsql, snake_case convention) backend; Angular 21 with NgRx Signal Store frontend. Backend work happens in the `HRMS-Backend-v1` worktree at `C:\onevoNew\HRMS-Backend-v1\.worktrees\bulk-employee-onboarding` (branch `feat/bulk-employee-onboarding`) — **run every backend command from that directory, not the main checkout**. Frontend work happens in `C:\onevoNew\Hrms--Web-application---front-end---v1` (branch `feature/employee-management-phase1-foundation`, plain checkout, no worktree).

## Global Constraints

- `reports_to_employee_id` (on both `position_assignments` and `onboarding_drafts`) is a plain nullable `uuid` column with **no FK constraint** — validity ("is this employee a current active holder of the target position") is time-varying and enforced in application code, not the database.
- `RebuildAsync` runs synchronously, inline, immediately after the triggering write commits — not a background job (user-confirmed).
- The disambiguating question is asked identically in all three assignment-creation flows (single-employee onboarding, bulk onboarding, Change Position) — never bulk-only.
- Do not reinstate `employees.manager_id` (dropped deliberately in migration `20260805090249_RemoveLegacyEmployeeJobTitleAndManagerFields`) and do not use `ManagementCoverageRecord` for this (it disambiguates positions, not individual employees — confirmed unusable during brainstorming).
- New endpoint route follows `PositionsController`'s actual convention: `api/v1/org/legal-entities/{legalEntityId:guid}/positions/{positionId}/active-holders`, `[RequirePermission("org:read")]` — not the `employees:read`/`api/v1/onboarding/...` shape the original design doc guessed.
- Migration commands run from the worktree's backend repo root: `dotnet ef migrations add <Name> --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api`.

---

## Task 1: `PositionAssignment.ReportsToEmployeeId` column + entity + architecture test

**Files:**
- Modify: `src/ONEVO.Domain/Features/CoreHr/PositionAssignment/Entities/PositionAssignment.cs`
- Modify: `tests/ONEVO.Tests.Architecture/PositionAssignmentArchitectureTests.cs`
- Create (via `dotnet ef migrations add`): `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddPositionAssignmentReportsToEmployeeId.cs`

**Interfaces:**
- Produces: `PositionAssignment.ReportsToEmployeeId` (`Guid?`) — consumed by Task 2 (repository writes), Task 4 (closure rebuild), Task 6.

- [ ] **Step 1: Read the current architecture test to see its exact shape assertion**

Open `tests/ONEVO.Tests.Architecture/PositionAssignmentArchitectureTests.cs` and find the test named `PositionAssignment_HasExpectedShape` (or equivalent reflection-based property-list assertion). Note its exact expected property list — you'll extend it in Step 2, not replace it.

- [ ] **Step 2: Update the architecture test to expect the new property**

Add `"ReportsToEmployeeId"` to the expected property-name list in `PositionAssignment_HasExpectedShape` (whatever the existing array/collection literal is called), preserving every other existing entry exactly as-is. Confirm the test's "no `ManagerId`/no `JobTitleId`" guard (if present) is unaffected — `ReportsToEmployeeId` is deliberately not `ManagerId`.

- [ ] **Step 3: Run the architecture test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~PositionAssignment_HasExpectedShape"`
Expected: FAIL — actual property list is missing `ReportsToEmployeeId`.

- [ ] **Step 4: Add the property to the entity**

Edit `src/ONEVO.Domain/Features/CoreHr/PositionAssignment/Entities/PositionAssignment.cs`:

```csharp
public class PositionAssignment : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public Guid PositionId { get; set; }
    public string AssignmentKind { get; set; } = PositionAssignmentKind.PrimaryEmployment;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string AssignmentStatus { get; set; } = PositionAssignmentStatus.Active;
    public string? ChangeReason { get; set; }
    public Guid? ReportsToEmployeeId { get; set; }
}
```

- [ ] **Step 5: Run the architecture test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~PositionAssignment_HasExpectedShape"`
Expected: PASS

- [ ] **Step 6: Generate the migration**

Run from the worktree repo root:
```bash
dotnet ef migrations add AddPositionAssignmentReportsToEmployeeId --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
```

Open the generated `Up()`/`Down()` and confirm it matches this shape (no FK, no index — matches the `AddInvitationTokenPositionAssignmentId` precedent):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<Guid>(
        name: "reports_to_employee_id",
        table: "position_assignments",
        type: "uuid",
        nullable: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(
        name: "reports_to_employee_id",
        table: "position_assignments");
}
```

If the tool generated an index or FK you didn't ask for, remove it manually from the migration file so it matches the above exactly — this column is intentionally unconstrained (see Global Constraints).

- [ ] **Step 7: Apply the migration locally and confirm it runs clean**

Run: `dotnet ef database update --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api`
Expected: migration applies with no errors; `\d position_assignments` in psql (or equivalent) shows the new nullable `reports_to_employee_id uuid` column.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/PositionAssignment/Entities/PositionAssignment.cs tests/ONEVO.Tests.Architecture/PositionAssignmentArchitectureTests.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add PositionAssignment.ReportsToEmployeeId column"
```

---

## Task 2: Thread `reportsToEmployeeId` through the assignment-creation repository methods

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/PositionAssignmentRlsIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 1's `PositionAssignment.ReportsToEmployeeId`.
- Produces: `TryReservePositionAssignmentAsync(tenantId, employeeId, positionId, effectiveFrom, createdById, reportsToEmployeeId, ct)` and `TryCreateActiveAssignmentAsync(tenantId, employeeId, positionId, effectiveFrom, createdById, reportsToEmployeeId, ct)` — new trailing-before-`ct` `Guid? reportsToEmployeeId` parameter on both. Consumed by Task 9 (onboarding finalize) and Task 11 (Change Position).

- [ ] **Step 1: Write the failing integration test**

Add to `tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/PositionAssignmentRlsIntegrationTests.cs` (mirror the existing seeding/fixture pattern already in that file — same `IAsyncLifetime`/Testcontainers setup, same tenant/employee/position seed helpers already used by neighboring tests in the file):

```csharp
[Fact]
public async Task TryCreateActiveAssignmentAsync_Persists_ReportsToEmployeeId_When_Provided()
{
    var managerId = await SeedEmployeeAsync(); // use the same seeding helper other tests in this file use
    var employeeId = await SeedEmployeeAsync();
    var positionId = await SeedPositionAsync(maxOccupancy: 5); // pooled

    var assignmentId = await _repository.TryCreateActiveAssignmentAsync(
        _tenantId, employeeId, positionId, DateOnly.FromDateTime(DateTime.UtcNow), _createdById,
        reportsToEmployeeId: managerId, CancellationToken.None);

    assignmentId.Should().NotBeNull();

    var stored = await _dbContext.PositionAssignments.AsNoTracking()
        .SingleAsync(pa => pa.Id == assignmentId!.Value);
    stored.ReportsToEmployeeId.Should().Be(managerId);
}
```

(Use whatever seeding helper names and assertion library — `FluentAssertions` vs `Assert.Equal` — the rest of the file already uses; match its existing style exactly rather than the sketch above if it differs.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TryCreateActiveAssignmentAsync_Persists_ReportsToEmployeeId_When_Provided"`
Expected: FAIL — compile error, since the new overload doesn't exist yet.

- [ ] **Step 3: Update the interface**

In `IPositionAssignmentRepository.cs`, change both signatures:

```csharp
Task<Guid?> TryReservePositionAssignmentAsync(
    Guid tenantId,
    Guid employeeId,
    Guid positionId,
    DateOnly effectiveFrom,
    Guid createdById,
    Guid? reportsToEmployeeId,
    CancellationToken ct = default);
```

```csharp
Task<Guid?> TryCreateActiveAssignmentAsync(
    Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
    Guid? reportsToEmployeeId, CancellationToken ct = default);
```

Update the XML doc comments above each to note the new parameter, preserving the existing seat-counting explanation text.

- [ ] **Step 4: Update the EF implementation**

In `EfPositionAssignmentRepository.cs`, both methods currently build the insert via `ExecuteSqlInterpolatedAsync` with an explicit column list. Add `reports_to_employee_id` to both the column list and the `SELECT` list, and add the new parameter:

```csharp
public async Task<Guid?> TryReservePositionAssignmentAsync(
    Guid tenantId, Guid employeeId, Guid positionId, DateOnly effectiveFrom, Guid createdById,
    Guid? reportsToEmployeeId, CancellationToken ct = default)
{
    var newId = Guid.NewGuid();
    var now = _clock.UtcNow;

    var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO position_assignments
            (id, tenant_id, employee_id, position_id, assignment_kind, effective_from,
             assignment_status, created_by_id, created_at, is_deleted, reports_to_employee_id)
        SELECT {newId}, {tenantId}, {employeeId}, {positionId}, {PositionAssignmentKind.PrimaryEmployment},
               {effectiveFrom}, {PositionAssignmentStatus.Planned}, {createdById}, {now}, false, {reportsToEmployeeId}
        WHERE ( /* existing capacity subquery — do not change */ )
    ", ct);

    return rowsAffected > 0 ? newId : null;
}
```

Apply the same column/value addition to `TryCreateActiveAssignmentAsync`'s insert (same shape, `PositionAssignmentStatus.Active` literal instead of `Planned`, same capacity subquery). Preserve the existing `UniqueViolation` → `UniqueConstraintConflictException` try/catch in `TryCreateActiveAssignmentAsync` unchanged.

- [ ] **Step 5: Fix every existing caller to pass the new parameter**

Grep for both method names across the worktree (`grep -rn "TryReservePositionAssignmentAsync\|TryCreateActiveAssignmentAsync" src/`) and add `reportsToEmployeeId: null` at each existing call site for now — Tasks 9 and 11 will change these `null`s to the real value. Do not skip any call site; a missed one is a compile error, not a silent bug, so the compiler will catch it, but confirm the build is clean before moving on.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TryCreateActiveAssignmentAsync_Persists_ReportsToEmployeeId_When_Provided"`
Expected: PASS

- [ ] **Step 7: Run the full unit test suite to catch any other broken callers**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: PASS. Any failures here are Moq setups for `TryReservePositionAssignmentAsync`/`TryCreateActiveAssignmentAsync` that need their `It.IsAny<Guid?>()` argument added — fix each, matching the existing Moq style in that test file (`_positionAssignmentRepository.Setup(r => r.TryCreateActiveAssignmentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))...`).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs tests/
git commit -m "feat: thread reportsToEmployeeId through position-assignment creation"
```

---

## Task 3: Active-holders lookup with work email

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/PositionAssignment/Models/PositionOccupancyPreview.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/PositionAssignment/` (new file, mirror an existing repository-adjacent test in this folder for style, or if the folder has none, mirror `PositionAssignmentRlsIntegrationTests.cs`'s seeding style as an integration test instead)

**Interfaces:**
- Consumes: existing `PositionAssignments`/`Employees` EF sets (same join `GetOccupancyPreviewsAsync` already does).
- Produces: `Task<IReadOnlyList<PositionActiveHolder>> GetActiveHoldersAsync(Guid tenantId, Guid positionId, CancellationToken ct = default)` on `IPositionAssignmentRepository`, returning `PositionActiveHolder(Guid EmployeeId, string FirstName, string LastName, string WorkEmail, Guid? AvatarFileId)`. Consumed by Task 7 (new endpoint), Task 9/Task 12 (SaveAsync/bulk row validation), Task 15/16 (frontend picker).

- [ ] **Step 1: Write the failing test**

Add a new test file `tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/PositionAssignmentActiveHoldersTests.cs`, mirroring `PositionAssignmentRlsIntegrationTests.cs`'s fixture/seeding pattern exactly:

```csharp
public class PositionAssignmentActiveHoldersTests : IAsyncLifetime
{
    // Copy the exact IAsyncLifetime/Testcontainers setup from PositionAssignmentRlsIntegrationTests.cs

    [Fact]
    public async Task GetActiveHoldersAsync_Returns_Only_Active_PrimaryEmployment_Holders_With_Email()
    {
        var positionId = await SeedPositionAsync(maxOccupancy: 3);
        var activeHolderId = await SeedEmployeeWithActiveAssignmentAsync(positionId);
        var endedHolderId = await SeedEmployeeWithEndedAssignmentAsync(positionId);

        var holders = await _repository.GetActiveHoldersAsync(_tenantId, positionId, CancellationToken.None);

        holders.Should().ContainSingle(h => h.EmployeeId == activeHolderId);
        holders.Should().NotContain(h => h.EmployeeId == endedHolderId);
        holders.Single().WorkEmail.Should().NotBeNullOrEmpty();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~GetActiveHoldersAsync_Returns_Only_Active_PrimaryEmployment_Holders_With_Email"`
Expected: FAIL — compile error, `GetActiveHoldersAsync` doesn't exist.

- [ ] **Step 3: Add the model**

In `PositionOccupancyPreview.cs`, add alongside the existing records:

```csharp
public sealed record PositionActiveHolder(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string WorkEmail,
    Guid? AvatarFileId);
```

- [ ] **Step 4: Add the interface method**

In `IPositionAssignmentRepository.cs`:

```csharp
/// <summary>Current active PrimaryEmployment holders of a position, with work email — used to
/// disambiguate a reporting-manager override (onboarding wizard picker, bulk-onboarding CSV
/// email match, Change Position picker) against who is actually eligible right now.</summary>
Task<IReadOnlyList<PositionActiveHolder>> GetActiveHoldersAsync(
    Guid tenantId, Guid positionId, CancellationToken ct = default);
```

- [ ] **Step 5: Implement it**

In `EfPositionAssignmentRepository.cs`, add a method following `GetOccupancyPreviewsAsync`'s existing join pattern but for a single position and including email:

```csharp
public async Task<IReadOnlyList<PositionActiveHolder>> GetActiveHoldersAsync(
    Guid tenantId, Guid positionId, CancellationToken ct = default)
{
    return await _db.PositionAssignments
        .AsNoTracking()
        .Where(pa => pa.TenantId == tenantId
            && pa.PositionId == positionId
            && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
            && pa.AssignmentStatus == PositionAssignmentStatus.Active)
        .Join(_db.Employees.AsNoTracking(),
            pa => pa.EmployeeId, e => e.Id,
            (pa, e) => new PositionActiveHolder(e.Id, e.FirstName, e.LastName, e.WorkEmail, e.AvatarFileId))
        .ToListAsync(ct);
}
```

(Adjust `e.WorkEmail`/`e.AvatarFileId` to whatever the actual `Employee` entity property names are if they differ — check `src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs` before writing this.)

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~GetActiveHoldersAsync_Returns_Only_Active_PrimaryEmployment_Holders_With_Email"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/PositionAssignment/Models/PositionOccupancyPreview.cs src/ONEVO.Application/Features/CoreHr/PositionAssignment/RepositoryInterfaces/IPositionAssignmentRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs tests/
git commit -m "feat: add GetActiveHoldersAsync for reporting-manager disambiguation"
```

---

## Task 4: Fix `RebuildAsync`'s pooled-position resolution

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeHierarchyClosureRepository.cs`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/EmployeeHierarchyClosure/` (new file — check if this folder exists; if not, create it alongside sibling `CoreHr/*` integration test folders)

**Interfaces:**
- Consumes: Task 1's `PositionAssignment.ReportsToEmployeeId`.
- Produces: corrected `RebuildAsync` behavior — consumed by Task 5/6 (wiring).

- [ ] **Step 1: Write the failing test**

Create `tests/ONEVO.Tests.Integration/CoreHr/EmployeeHierarchyClosure/EmployeeHierarchyClosureRebuildTests.cs`, mirroring the Testcontainers fixture pattern from `PositionAssignmentRlsIntegrationTests.cs`:

```csharp
public class EmployeeHierarchyClosureRebuildTests : IAsyncLifetime
{
    // Copy fixture setup from PositionAssignmentRlsIntegrationTests.cs

    [Fact]
    public async Task RebuildAsync_Resolves_Unique_Position_Target_Automatically()
    {
        var managerId = await SeedEmployeeWithActiveAssignmentAsync(await SeedPositionAsync(maxOccupancy: 1));
        var subordinatePositionId = await SeedPositionAsync(maxOccupancy: 1, reportsToPositionId: /* manager's position id */ default);
        var subordinateId = await SeedEmployeeWithActiveAssignmentAsync(subordinatePositionId);

        await _closureRepository.RebuildAsync(_tenantId, CancellationToken.None);

        var resolvedManagerId = await _closureRepository.GetDirectManagerEmployeeIdAsync(_tenantId, subordinateId, CancellationToken.None);
        resolvedManagerId.Should().Be(managerId);
    }

    [Fact]
    public async Task RebuildAsync_Leaves_No_Row_When_Pooled_Target_Has_No_Override()
    {
        var pooledPositionId = await SeedPositionAsync(maxOccupancy: 2, reportsToPositionId: null);
        await SeedEmployeeWithActiveAssignmentAsync(pooledPositionId); // holder 1
        await SeedEmployeeWithActiveAssignmentAsync(pooledPositionId); // holder 2
        var subordinatePositionId = await SeedPositionAsync(maxOccupancy: 1, reportsToPositionId: pooledPositionId);
        var subordinateId = await SeedEmployeeWithActiveAssignmentAsync(subordinatePositionId, reportsToEmployeeId: null);

        await _closureRepository.RebuildAsync(_tenantId, CancellationToken.None);

        var resolvedManagerId = await _closureRepository.GetDirectManagerEmployeeIdAsync(_tenantId, subordinateId, CancellationToken.None);
        resolvedManagerId.Should().BeNull();
    }

    [Fact]
    public async Task RebuildAsync_Resolves_Pooled_Target_Via_ReportsToEmployeeId_Override()
    {
        var pooledPositionId = await SeedPositionAsync(maxOccupancy: 2, reportsToPositionId: null);
        var chosenHolderId = await SeedEmployeeWithActiveAssignmentAsync(pooledPositionId);
        await SeedEmployeeWithActiveAssignmentAsync(pooledPositionId); // the other holder, not chosen
        var subordinatePositionId = await SeedPositionAsync(maxOccupancy: 1, reportsToPositionId: pooledPositionId);
        var subordinateId = await SeedEmployeeWithActiveAssignmentAsync(subordinatePositionId, reportsToEmployeeId: chosenHolderId);

        await _closureRepository.RebuildAsync(_tenantId, CancellationToken.None);

        var resolvedManagerId = await _closureRepository.GetDirectManagerEmployeeIdAsync(_tenantId, subordinateId, CancellationToken.None);
        resolvedManagerId.Should().Be(chosenHolderId);
    }
}
```

(Add `reportsToEmployeeId` and `reportsToPositionId` optional parameters to whatever seeding helpers you copy in, so these three tests can each set them up directly — match the existing helper signatures in `PositionAssignmentRlsIntegrationTests.cs` as closely as possible and extend rather than duplicate them if they're in a shared base class.)

- [ ] **Step 2: Run the tests to verify failure/wrong-result**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~EmployeeHierarchyClosureRebuildTests"`
Expected: the first test passes already (unique-target resolution already works today), but `RebuildAsync_Leaves_No_Row_When_Pooled_Target_Has_No_Override` and `RebuildAsync_Resolves_Pooled_Target_Via_ReportsToEmployeeId_Override` FAIL — today's code arbitrarily picks `g.First()` regardless of override, so the third test's `resolvedManagerId` will sometimes equal the *unchosen* holder instead of `chosenHolderId`, and the second test will get a `resolvedManagerId` instead of `null`.

- [ ] **Step 3: Fix the algorithm**

In `EfEmployeeHierarchyClosureRepository.cs`, replace the `positionIdToEmployeeAssignment` single-dictionary approach with a multi-holder-aware lookup:

```csharp
public async Task RebuildAsync(Guid tenantId, CancellationToken ct = default)
{
    var activeAssignments = await _db.PositionAssignments
        .AsNoTracking()
        .Where(pa => pa.TenantId == tenantId
            && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
            && pa.AssignmentStatus == PositionAssignmentStatus.Active)
        .ToListAsync(ct);

    var holdersByPositionId = activeAssignments
        .GroupBy(pa => pa.PositionId)
        .ToDictionary(g => g.Key, g => g.ToList());

    var positions = await _db.Positions
        .AsNoTracking()
        .Where(p => p.TenantId == tenantId)
        .ToDictionaryAsync(p => p.Id, ct);

    var newRows = new List<ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure>();
    var now = _clock.UtcNow;

    foreach (var assignment in activeAssignments)
    {
        var depth = 1;
        positions.TryGetValue(assignment.PositionId, out var ownPosition);
        var currentPositionId = ownPosition?.ReportsToPositionId;
        var currentReportsToEmployeeId = assignment.ReportsToEmployeeId;
        var visited = new HashSet<Guid> { assignment.PositionId };

        while (currentPositionId is not null
            && visited.Add(currentPositionId.Value)
            && holdersByPositionId.TryGetValue(currentPositionId.Value, out var holders))
        {
            ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment? ancestorAssignment = holders.Count switch
            {
                1 => holders[0],
                _ => currentReportsToEmployeeId is { } overrideId
                    ? holders.FirstOrDefault(h => h.EmployeeId == overrideId)
                    : null,
            };

            if (ancestorAssignment is null)
                break; // pooled target with no (or stale) override: stop here, no closure row for this link

            newRows.Add(new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure
            {
                TenantId = tenantId,
                AncestorEmployeeId = ancestorAssignment.EmployeeId,
                DescendantEmployeeId = assignment.EmployeeId,
                Depth = depth,
                SourcePositionAssignmentId = assignment.Id,
                GeneratedAt = now,
            });

            depth++;
            currentReportsToEmployeeId = ancestorAssignment.ReportsToEmployeeId;
            currentPositionId = positions.TryGetValue(currentPositionId.Value, out var ancestorPosition)
                ? ancestorPosition.ReportsToPositionId
                : null;
        }
    }

    var existing = await _db.EmployeeHierarchyClosures
        .Where(c => c.TenantId == tenantId)
        .ToListAsync(ct);

    _db.EmployeeHierarchyClosures.RemoveRange(existing);
    await _db.EmployeeHierarchyClosures.AddRangeAsync(newRows, ct);
    await _db.SaveChangesAsync(ct);
}
```

Note the walk now carries `currentReportsToEmployeeId` forward from the just-resolved ancestor's own assignment (not the original descendant's) at each depth — each link in the chain uses its own assignment's override, since a different ancestor further up the chain may itself sit under a different pooled position.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~EmployeeHierarchyClosureRebuildTests"`
Expected: all three PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeHierarchyClosureRepository.cs tests/
git commit -m "fix: resolve pooled-position ancestors via ReportsToEmployeeId instead of arbitrary pick"
```

---

## Task 5: Wire `RebuildAsync` into every `PositionAssignment` write path

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs`
- Test: extend `PositionAssignmentActiveHoldersTests.cs` or add a new integration test file

**Interfaces:**
- Consumes: `IEmployeeHierarchyClosureRepository.RebuildAsync` (Task 4).
- Produces: closure table stays current after every assignment mutation — consumed by Task 9/11's end-to-end behavior and Task 4's own tests (which called `RebuildAsync` directly; this task makes that call automatic).

- [ ] **Step 1: Write the failing test**

Add to `tests/ONEVO.Tests.Integration/CoreHr/PositionAssignment/PositionAssignmentActiveHoldersTests.cs` (or a new adjacent file):

```csharp
[Fact]
public async Task TryCreateActiveAssignmentAsync_Triggers_Closure_Rebuild()
{
    var managerId = await SeedEmployeeWithActiveAssignmentAsync(await SeedPositionAsync(maxOccupancy: 1));
    var subordinatePositionId = await SeedPositionAsync(maxOccupancy: 1, reportsToPositionId: /* manager's position id */ default);
    var subordinateId = await SeedEmployeeAsync(); // no assignment yet

    await _repository.TryCreateActiveAssignmentAsync(
        _tenantId, subordinateId, subordinatePositionId, DateOnly.FromDateTime(DateTime.UtcNow), _createdById,
        reportsToEmployeeId: null, CancellationToken.None);

    // No explicit RebuildAsync call here — this is the point of the test.
    var resolvedManagerId = await _closureRepository.GetDirectManagerEmployeeIdAsync(_tenantId, subordinateId, CancellationToken.None);
    resolvedManagerId.Should().Be(managerId);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TryCreateActiveAssignmentAsync_Triggers_Closure_Rebuild"`
Expected: FAIL — `resolvedManagerId` is null, since nothing calls `RebuildAsync` yet.

- [ ] **Step 3: Inject the closure repository and call it after every mutation**

In `EfPositionAssignmentRepository.cs`, add a constructor dependency:

```csharp
private readonly IEmployeeHierarchyClosureRepository _closureRepository;

public EfPositionAssignmentRepository(ApplicationDbContext db, IDateTimeProvider clock, IEmployeeHierarchyClosureRepository closureRepository)
{
    _db = db;
    _clock = clock;
    _closureRepository = closureRepository;
}
```

Then, at the end of each of `TryReservePositionAssignmentAsync`, `ActivatePlannedAsync`, `CancelPlannedAsync`, `TryCreateActiveAssignmentAsync`, and `EndActiveAsync` — **only on the success path** (non-null/non-false return) — add:

```csharp
if (rowsAffected > 0) // or the method's existing success condition
{
    await _closureRepository.RebuildAsync(tenantId, ct);
}
```

Place this call after the write has actually committed in each method (i.e. after any existing `SaveChangesAsync`/`ExecuteSqlInterpolatedAsync` that persists the row — `RebuildAsync` reads back from the database, so it must run after the write is durable, not before).

- [ ] **Step 4: Verify no circular DI registration issue**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: builds clean. (`IEmployeeHierarchyClosureRepository`'s own implementation doesn't depend on `IPositionAssignmentRepository`, so there's no cycle — confirm this by checking `EfEmployeeHierarchyClosureRepository`'s constructor before moving on.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TryCreateActiveAssignmentAsync_Triggers_Closure_Rebuild"`
Expected: PASS

- [ ] **Step 6: Run the full integration suite**

Run: `dotnet test tests/ONEVO.Tests.Integration`
Expected: PASS. This is the first point where every existing test that creates a `PositionAssignment` now also exercises the (fixed) rebuild — watch specifically for any test relying on the closure table being empty, and update it if so (it shouldn't be, since nothing tested closure contents before this task).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfPositionAssignmentRepository.cs tests/
git commit -m "feat: rebuild employee hierarchy closure after every position-assignment write"
```

---

## Task 6: Wire `RebuildAsync` into `ReportsToPositionId` changes

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandHandlerTests.cs` (find and extend the existing file — do not create a new one if it already exists)

**Interfaces:**
- Consumes: `IEmployeeHierarchyClosureRepository.RebuildAsync` (Task 4).

- [ ] **Step 1: Write the failing test**

Find the existing `UpdatePositionCommandHandlerTests.cs` and add, matching its existing Moq-based style exactly:

```csharp
[Fact]
public async Task Handle_Calls_RebuildAsync_When_ReportsToPositionId_Changes()
{
    // Arrange using the file's existing CreateHandler()/default-happy-path setup, with an
    // existing position whose ReportsToPositionId differs from the command's requested value.

    await _handler.Handle(command, CancellationToken.None);

    _closureRepository.Verify(c => c.RebuildAsync(_tenantId, It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task Handle_Does_Not_Call_RebuildAsync_When_ReportsToPositionId_Unchanged()
{
    // Arrange with a command whose ReportsToPositionId equals the existing position's current value.

    await _handler.Handle(command, CancellationToken.None);

    _closureRepository.Verify(c => c.RebuildAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UpdatePositionCommandHandlerTests"`
Expected: FAIL — compile error, since `_closureRepository` mock and constructor wiring don't exist yet in the handler/test.

- [ ] **Step 3: Inject the closure repository into the handler**

In `UpdatePositionCommandHandler.cs`, add `IEmployeeHierarchyClosureRepository` to the constructor (matching however the file's existing dependencies are injected — same DI pattern as `_positions`, `_departments`, etc.).

- [ ] **Step 4: Call `RebuildAsync` inside the existing `reportsToChanged` block**

Find the existing `if (reportsToChanged)` block (already computes `oldReportsToPositionId != request.ReportsToPositionId` and writes `PositionReportingHistory` + syncs `ManagementCoverageRecord`). After the existing `SaveChangesAsync` call for this handler (confirm exactly where persistence happens — it may be a single `SaveChangesAsync` at the end of `Handle`, not inside the `if` block itself), add:

```csharp
if (reportsToChanged)
{
    await _closureRepository.RebuildAsync(tenantId, ct);
}
```

Place this after whatever `SaveChangesAsync` call persists the `ReportsToPositionId` change — `RebuildAsync` must read the already-committed new value.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UpdatePositionCommandHandlerTests"`
Expected: PASS

- [ ] **Step 6: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandHandler.cs tests/
git commit -m "feat: rebuild employee hierarchy closure when a position's ReportsToPositionId changes"
```

---

## Task 7: `GET .../positions/{id}/active-holders` endpoint

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetActiveHolders/GetActiveHoldersQuery.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetActiveHolders/GetActiveHoldersQueryHandler.cs`
- Create: `src/ONEVO.Api/Contracts/OrgStructure/Positions/ActiveHolderViewModel.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/Queries/GetActiveHolders/GetActiveHoldersQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.GetActiveHoldersAsync` (Task 3).
- Produces: `GET api/v1/org/legal-entities/{legalEntityId}/positions/{positionId}/active-holders` → `IReadOnlyList<ActiveHolderViewModel>`. Consumed by frontend Task 15/16 (picker), and used server-side by Task 12's bulk row validator (calling the repository method directly, not this HTTP endpoint).

- [ ] **Step 1: Write the failing unit test**

```csharp
public class GetActiveHoldersQueryHandlerTests
{
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private GetActiveHoldersQueryHandler CreateHandler() =>
        new(_assignments.Object, _positions.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_Returns_Holders_From_Repository()
    {
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, TenantId = tenantId, Name = "Team Lead" });
        _assignments.Setup(a => a.GetActiveHoldersAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder> { new(Guid.NewGuid(), "Jane", "Doe", "jane@acme.test", null) });

        var result = await CreateHandler().Handle(new GetActiveHoldersQuery(legalEntityId, positionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_Position_Missing()
    {
        var tenantId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        var result = await CreateHandler().Handle(new GetActiveHoldersQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsNotFound.Should().BeTrue();
    }
}
```

(Match whichever `Result<T>` status-check property names — `IsSuccess`/`IsNotFound` vs. something else — the codebase's `Result<T>` type actually exposes; check `GetCoverageByTargetQueryHandler.cs`'s test if one exists, or the `Result<T>` class itself, before finalizing these assertions.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetActiveHoldersQueryHandlerTests"`
Expected: FAIL — compile error, types don't exist yet.

- [ ] **Step 3: Create the query, response model, and handler**

`GetActiveHoldersQuery.cs`:
```csharp
public record GetActiveHoldersQuery(Guid LegalEntityId, Guid PositionId) : IRequest<Result<IReadOnlyList<ActiveHolderViewModel>>>;
```

`ActiveHolderViewModel.cs` (in `Api/Contracts`, following the pattern of other `*ViewModel` records in that folder):
```csharp
public record ActiveHolderViewModel(Guid EmployeeId, string FirstName, string LastName, string WorkEmail, Guid? AvatarFileId);
```

`GetActiveHoldersQueryHandler.cs`, modeled on `GetPositionCoverageQueryHandler.cs`'s auth/tenant/not-found checks:
```csharp
public class GetActiveHoldersQueryHandler : IRequestHandler<GetActiveHoldersQuery, Result<IReadOnlyList<ActiveHolderViewModel>>>
{
    private readonly IPositionAssignmentRepository _assignments;
    private readonly IPositionRepository _positions;
    private readonly ICurrentUser _currentUser;

    public GetActiveHoldersQueryHandler(IPositionAssignmentRepository assignments, IPositionRepository positions, ICurrentUser currentUser)
    {
        _assignments = assignments;
        _positions = positions;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ActiveHolderViewModel>>> Handle(GetActiveHoldersQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ActiveHolderViewModel>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ActiveHolderViewModel>>.Forbidden("Tenant context missing.");

        var position = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (position == null)
            return Result<IReadOnlyList<ActiveHolderViewModel>>.NotFound("Position not found.");

        var holders = await _assignments.GetActiveHoldersAsync(tenantId, request.PositionId, ct);

        var viewModels = holders
            .Select(h => new ActiveHolderViewModel(h.EmployeeId, h.FirstName, h.LastName, h.WorkEmail, h.AvatarFileId))
            .ToList();

        return Result<IReadOnlyList<ActiveHolderViewModel>>.Success(viewModels);
    }
}
```

Note the response DTO note in Global Constraints: this uses `Api/Contracts` (`ActiveHolderViewModel`), not `Application/DTOs`, matching the architecture-doc fix already recorded in the companion bulk-onboarding spec (§7 of that doc) — a controller-facing wire shape, distinct from any Application-layer response type.

- [ ] **Step 4: Add the controller action**

In `PositionsController.cs`, add (matching the file's existing action style — `[RequirePermission("org:read")]`, route relative to the controller's `[Route("api/v1/org/legal-entities/{legalEntityId:guid}/positions")]` base):

```csharp
[HttpGet("{positionId:guid}/active-holders")]
[RequirePermission("org:read")]
public async Task<IActionResult> GetActiveHolders(Guid legalEntityId, Guid positionId, CancellationToken ct)
{
    var result = await _mediator.Send(new GetActiveHoldersQuery(legalEntityId, positionId), ct);
    return result.ToActionResult(this); // match whichever Result<T>-to-IActionResult helper the rest of this controller's actions already use
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetActiveHoldersQueryHandlerTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetActiveHolders/ src/ONEVO.Api/Contracts/OrgStructure/Positions/ActiveHolderViewModel.cs tests/
git commit -m "feat: add GET positions/{id}/active-holders endpoint"
```

---

## Task 8: `OnboardingDraft.ReportsToEmployeeId` column

**Files:**
- Modify: `src/ONEVO.Domain/Features/CoreHr/OnboardingDraft/Entities/OnboardingDraft.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/OnboardingDraft/OnboardingDraftConfiguration.cs` (only if it explicitly configures other nullable Guid fields like `SelectedTemplateId` — otherwise no config change needed, EF's snake_case convention maps it automatically)
- Create (via `dotnet ef migrations add`): new migration in `src/ONEVO.Infrastructure/Migrations/`

**Interfaces:**
- Produces: `OnboardingDraft.ReportsToEmployeeId` (`Guid?`) — consumed by Task 9 (SaveAsync persists it) and Task 10 (FinalizeAsync reads it).

- [ ] **Step 1: Add the property**

In `OnboardingDraft.cs`, add `public Guid? ReportsToEmployeeId { get; set; }` alongside the existing `PositionId`/`DepartmentId` properties.

- [ ] **Step 2: Check the configuration file**

Open `OnboardingDraftConfiguration.cs`. If `SelectedTemplateId` (a nullable Guid with no `HasOne`/FK per the research findings) has an explicit `builder.Property(...)` line, add a matching one for `ReportsToEmployeeId`. If `SelectedTemplateId` has no explicit configuration at all (pure convention), leave `ReportsToEmployeeId` unconfigured too — don't add a `HasOne` FK (matches Global Constraints: no FK on this field anywhere).

- [ ] **Step 3: Generate and review the migration**

```bash
dotnet ef migrations add AddOnboardingDraftReportsToEmployeeId --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
```

Confirm the generated `Up()` is exactly:
```csharp
migrationBuilder.AddColumn<Guid>(
    name: "reports_to_employee_id",
    table: "onboarding_drafts",
    type: "uuid",
    nullable: true);
```
Remove any auto-generated index/FK, same as Task 1 Step 6.

- [ ] **Step 4: Apply and verify**

Run: `dotnet ef database update --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api`
Expected: clean apply.

- [ ] **Step 5: Build to confirm nothing else broke**

Run: `dotnet build`
Expected: succeeds (no test yet for this task in isolation — Task 9 covers the behavior).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/OnboardingDraft/Entities/OnboardingDraft.cs src/ONEVO.Infrastructure/Migrations/ src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/OnboardingDraft/OnboardingDraftConfiguration.cs
git commit -m "feat: add OnboardingDraft.ReportsToEmployeeId column"
```

---

## Task 9: `SaveOnboardingDraftCommand` validates and persists `reportsToEmployeeId`

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/SaveOnboardingDraft/SaveOnboardingDraftCommand.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/SaveOnboardingDraftCommandHandlerTests.cs` (find and extend existing file)

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.GetActiveHoldersAsync` (Task 3).
- Produces: `SaveOnboardingDraftCommand` gains `Guid? ReportsToEmployeeId`; validated and written to `draft.ReportsToEmployeeId` (Task 8's column).

- [ ] **Step 1: Write the failing tests**

Add to the existing `SaveOnboardingDraftCommandHandlerTests.cs` (or the file testing `OnboardingDraftWriteService.SaveAsync` directly, per whichever the existing file actually targets — the research found `FinalizeOnboardingDraftCommandHandlerTests.cs` constructs the write service directly via a `CreateWriteService()` factory; mirror that same factory if `SaveOnboardingDraftCommandHandlerTests.cs` also does):

```csharp
[Fact]
public async Task SaveAsync_Requires_ReportsToEmployeeId_When_Position_Target_Is_Pooled()
{
    var positionId = Guid.NewGuid();
    var pooledTargetId = Guid.NewGuid();
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = positionId, ReportsToPositionId = pooledTargetId });
    _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder>
        {
            new(Guid.NewGuid(), "A", "One", "a@acme.test", null),
            new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
        });

    var command = BuildValidCommand() with { PositionId = positionId, ReportsToEmployeeId = null };

    var result = await CreateWriteService().SaveAsync(_tenantId, _actingUserId, command, CancellationToken.None);

    result.IsUnprocessableEntity.Should().BeTrue(); // match the actual Result<T> failure-kind property used elsewhere in this file for validation failures
}

[Fact]
public async Task SaveAsync_Rejects_ReportsToEmployeeId_Not_A_Current_Active_Holder()
{
    var positionId = Guid.NewGuid();
    var pooledTargetId = Guid.NewGuid();
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = positionId, ReportsToPositionId = pooledTargetId });
    _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder> { new(Guid.NewGuid(), "A", "One", "a@acme.test", null) });

    var command = BuildValidCommand() with { PositionId = positionId, ReportsToEmployeeId = Guid.NewGuid() /* not in the holder list */ };

    var result = await CreateWriteService().SaveAsync(_tenantId, _actingUserId, command, CancellationToken.None);

    result.IsUnprocessableEntity.Should().BeTrue();
}

[Fact]
public async Task SaveAsync_Persists_ReportsToEmployeeId_When_Valid()
{
    var positionId = Guid.NewGuid();
    var pooledTargetId = Guid.NewGuid();
    var chosenManagerId = Guid.NewGuid();
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = positionId, ReportsToPositionId = pooledTargetId });
    _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder> { new(chosenManagerId, "A", "One", "a@acme.test", null) });

    var command = BuildValidCommand() with { PositionId = positionId, ReportsToEmployeeId = chosenManagerId };

    var result = await CreateWriteService().SaveAsync(_tenantId, _actingUserId, command, CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    _draftRepository.Verify(r => r.AddAsync(It.Is<OnboardingDraft>(d => d.ReportsToEmployeeId == chosenManagerId), It.IsAny<CancellationToken>()), Times.Once);
    // Adjust the Verify target (AddAsync vs UpdateAsync, or however the write service persists) to match the file's actual repository call.
}

[Fact]
public async Task SaveAsync_Ignores_ReportsToEmployeeId_When_Position_Target_Has_Single_Holder()
{
    var positionId = Guid.NewGuid();
    var uniqueTargetId = Guid.NewGuid();
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), positionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = positionId, ReportsToPositionId = uniqueTargetId });
    _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, uniqueTargetId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder> { new(Guid.NewGuid(), "A", "One", "a@acme.test", null) });

    var command = BuildValidCommand() with { PositionId = positionId, ReportsToEmployeeId = null };

    var result = await CreateWriteService().SaveAsync(_tenantId, _actingUserId, command, CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
}
```

(`BuildValidCommand()` should be a small local helper you add at the top of the test class if one doesn't already exist, returning a `SaveOnboardingDraftCommand` with every currently-required field filled from the file's existing happy-path setup — check the existing tests in this file for what they already construct and factor it out if it's duplicated inline.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SaveOnboardingDraftCommandHandlerTests"`
Expected: FAIL — compile error (`ReportsToEmployeeId` doesn't exist on the command record yet).

- [ ] **Step 3: Add the field to the command**

In `SaveOnboardingDraftCommand.cs`, add `Guid? ReportsToEmployeeId` as a new positional parameter (append at the end to avoid reordering every existing positional-record call site):

```csharp
public record SaveOnboardingDraftCommand(
    Guid? DraftId, string FirstName, string LastName, string WorkEmail,
    Guid LegalEntityId, Guid? DepartmentId, Guid? PositionId, string EmploymentType,
    DateOnly StartDate, string? EmployeeNumber, int WorkModeId, Guid? SelectedTemplateId,
    string? EditedTasksJson, string LastSavedStep, string? IfMatchVersion,
    Guid? ReportsToEmployeeId) : IRequest<Result<OnboardingDraftResponse>>;
```

- [ ] **Step 4: Add validation + persistence in `SaveAsync`**

In `OnboardingDraftWriteService.SaveAsync` (around the existing position/department existence checks, lines ~131-136 per the research findings), after the position is resolved and confirmed to exist:

```csharp
if (request.PositionId is { } positionId)
{
    var position = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, positionId, ct);
    // ... existing null/inactive checks stay as-is ...

    if (position?.ReportsToPositionId is { } reportsToPositionId)
    {
        var activeHolders = await _positionAssignments.GetActiveHoldersAsync(tenantId, reportsToPositionId, ct);
        if (activeHolders.Count > 1)
        {
            if (request.ReportsToEmployeeId is not { } chosenManagerId)
                return Result<OnboardingDraftResponse>.UnprocessableEntity(
                    "This position's manager position has multiple current holders — select which one this employee reports to.");

            if (!activeHolders.Any(h => h.EmployeeId == chosenManagerId))
                return Result<OnboardingDraftResponse>.UnprocessableEntity(
                    "The selected reporting manager is not a current holder of this position's manager position.");
        }
    }
}
```

Then, alongside the existing field assignments that build/update the `OnboardingDraft` entity (research findings: lines ~223-238), add:

```csharp
draft.ReportsToEmployeeId = request.ReportsToEmployeeId;
```

(If the target position has 0 or 1 holders, `request.ReportsToEmployeeId` is simply assigned through unchecked — matches Global Constraints' "ignored if provided, not rejected" rule for the unambiguous case.)

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SaveOnboardingDraftCommandHandlerTests"`
Expected: PASS

- [ ] **Step 6: Fix any other broken callers of the command's positional constructor**

Run: `dotnet build`
Expected: any other place constructing `new SaveOnboardingDraftCommand(...)` positionally (search `grep -rn "new SaveOnboardingDraftCommand("`) needs `null` (or a real value, for Task 13's bulk-onboarding caller specifically) appended as the last argument.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/SaveOnboardingDraft/SaveOnboardingDraftCommand.cs src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs tests/
git commit -m "feat: validate and persist ReportsToEmployeeId on SaveOnboardingDraft"
```

---

## Task 10: `FinalizeAsync` threads `ReportsToEmployeeId` into the assignment, plus response DTOs

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/DTOs/Responses/OnboardingDraftResponse.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingDraftRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs`

**Interfaces:**
- Consumes: Task 2's `TryReservePositionAssignmentAsync(..., reportsToEmployeeId, ct)`, Task 8's `OnboardingDraft.ReportsToEmployeeId`.

- [ ] **Step 1: Write the failing test**

Add to `FinalizeOnboardingDraftCommandHandlerTests.cs`, using its existing `CreateWriteService()` factory and ~15-mock happy-path defaults:

```csharp
[Fact]
public async Task FinalizeAsync_Passes_Draft_ReportsToEmployeeId_Into_Assignment_Reservation()
{
    var chosenManagerId = Guid.NewGuid();
    var draft = BuildValidDraft() with { ReportsToEmployeeId = chosenManagerId }; // adjust to however the file constructs its default draft fixture
    _draftRepository.Setup(r => r.GetByIdAsync(_tenantId, draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

    await CreateWriteService().FinalizeAsync(_tenantId, _actingUserId, draft.Id, CancellationToken.None);

    _positionAssignments.Verify(a => a.TryReservePositionAssignmentAsync(
        _tenantId, It.IsAny<Guid>(), draft.PositionId!.Value, draft.StartDate, _actingUserId, chosenManagerId, It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~FinalizeAsync_Passes_Draft_ReportsToEmployeeId_Into_Assignment_Reservation"`
Expected: FAIL — the existing call site passes a hardcoded `null` (from Task 2 Step 5) instead of `draft.ReportsToEmployeeId`.

- [ ] **Step 3: Update the call site**

In `OnboardingDraftWriteService.FinalizeAsync` / `FinalizeImmediatelyAsync` (research findings, lines ~518-525):

```csharp
Guid? reservedAssignmentId = null;
if (position is not null)
{
    reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
        draft.TenantId, employeeId, position.Id, draft.StartDate, actingUserId, draft.ReportsToEmployeeId, ct);
    if (reservedAssignmentId is null)
        return Result<FinalizeOnboardingDraftResponse>.Conflict("This position has reached its capacity.");
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~FinalizeAsync_Passes_Draft_ReportsToEmployeeId_Into_Assignment_Reservation"`
Expected: PASS

- [ ] **Step 5: Add the field to the response DTO and repository projection**

In `OnboardingDraftResponse.cs`, append `Guid? ReportsToEmployeeId` to the existing 21-positional-param record (at the end, to avoid reordering existing call sites).

In `EfOnboardingDraftRepository.cs`'s `GetResponseByIdAsync` inline `.Select(d => new OnboardingDraftResponse(...))` projection, add `d.ReportsToEmployeeId` in the matching position at the end of the constructor call.

- [ ] **Step 6: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: PASS — fix any other `new OnboardingDraftResponse(...)` positional call sites the compiler flags (`grep -rn "new OnboardingDraftResponse("`).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs src/ONEVO.Application/Features/CoreHr/OnboardingDraft/DTOs/Responses/OnboardingDraftResponse.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingDraftRepository.cs tests/
git commit -m "feat: thread draft ReportsToEmployeeId into position-assignment reservation at finalize"
```

---

## Task 11: `ChangeEmployeePosition` asks for the manager when needed

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommand.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ChangeEmployeePositionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: Task 2's repository signatures, Task 3's `GetActiveHoldersAsync`.

- [ ] **Step 1: Write the failing tests**

Add to the existing test file, matching its `CreateHandler()`/`FakeUnitOfWork` pattern:

```csharp
[Fact]
public async Task Handle_Requires_ReportsToEmployeeId_When_New_Position_Target_Is_Pooled()
{
    var pooledTargetId = Guid.NewGuid();
    _positions.Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, It.IsAny<Guid>(), _newPositionId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Position { Id = _newPositionId, ReportsToPositionId = pooledTargetId, RequiresApproval = false });
    _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder>
        {
            new(Guid.NewGuid(), "A", "One", "a@acme.test", null),
            new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
        });

    var command = new ChangeEmployeePositionCommand(_employeeId, _newPositionId, _effectiveFrom, "Transfer", ReportsToEmployeeId: null);

    var result = await CreateHandler().Handle(command, CancellationToken.None);

    result.IsUnprocessableEntity.Should().BeTrue();
}

[Fact]
public async Task Handle_Passes_ReportsToEmployeeId_Into_TryCreateActiveAssignmentAsync_For_Immediate_Changes()
{
    var chosenManagerId = Guid.NewGuid();
    // Arrange a non-approval-required position change (existing happy-path setup for the immediate branch).

    var command = new ChangeEmployeePositionCommand(_employeeId, _newPositionId, _effectiveFrom, "Transfer", chosenManagerId);
    await CreateHandler().Handle(command, CancellationToken.None);

    _positionAssignments.Verify(a => a.TryCreateActiveAssignmentAsync(
        _tenantId, _employeeId, _newPositionId, _effectiveFrom, It.IsAny<Guid>(), chosenManagerId, It.IsAny<CancellationToken>()),
        Times.Once);
}
```

(Add a similar reservation-branch test verifying `TryReservePositionAssignmentAsync` receives `chosenManagerId` for the approval-required path, mirroring whatever existing test already exercises that branch.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ChangeEmployeePositionCommandHandlerTests"`
Expected: FAIL — compile error, `ChangeEmployeePositionCommand` has no `ReportsToEmployeeId` parameter yet.

- [ ] **Step 3: Add the field to the command**

```csharp
public record ChangeEmployeePositionCommand(
    Guid EmployeeId, Guid PositionId, DateOnly EffectiveFrom, string ChangeReason,
    Guid? ReportsToEmployeeId) : IRequest<Result<ChangeEmployeePositionResponse>>;
```

- [ ] **Step 4: Add validation and thread the value through both branches**

In `ChangeEmployeePositionCommandHandler.cs`, after the position is resolved (before either branch), add the same validation shape as Task 9 Step 4:

```csharp
if (position.ReportsToPositionId is { } reportsToPositionId)
{
    var activeHolders = await _positionAssignmentRepository.GetActiveHoldersAsync(tenantId, reportsToPositionId, ct);
    if (activeHolders.Count > 1)
    {
        if (request.ReportsToEmployeeId is not { } chosenManagerId)
            return Result<ChangeEmployeePositionResponse>.UnprocessableEntity(
                "This position's manager position has multiple current holders — select which one this employee reports to.");
        if (!activeHolders.Any(h => h.EmployeeId == chosenManagerId))
            return Result<ChangeEmployeePositionResponse>.UnprocessableEntity(
                "The selected reporting manager is not a current holder of this position's manager position.");
    }
}
```

Then update both existing call sites (research findings' two exact snippets) to pass `request.ReportsToEmployeeId`:

```csharp
var reservedAssignmentId = await _positionAssignmentRepository.TryCreateActiveAssignmentAsync(
    tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, request.ReportsToEmployeeId, txnCt);
```

```csharp
var reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
    tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, request.ReportsToEmployeeId, txnCt);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ChangeEmployeePositionCommandHandlerTests"`
Expected: PASS

- [ ] **Step 6: Fix the validator and any other callers**

Check `ChangeEmployeePositionCommandValidator.cs` — no new rule is needed there (the pooled-target check is handler-level, matching Task 9's pattern of keeping position-conditional logic out of FluentValidation), but confirm the validator's constructor call for the record (if it constructs one in a test) compiles. Run `dotnet build` and fix any other positional-constructor call sites.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ tests/
git commit -m "feat: ask for reporting manager on Change Position when target is pooled"
```

---

## Task 12: Bulk onboarding — resolve "Reporting Manager" column by email

**Files:**
- Modify: `src/ONEVO.Domain/Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatchRow.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding/BulkOnboardingBatchRowConfiguration.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Services/IBulkOnboardingRowValidator.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Services/BulkOnboardingRowValidator.cs`
- Modify: the column-mapping suggester file (`ColumnMappingSuggester.cs` — confirm exact path via `grep -rln "ColumnMappingSuggester" src/`)
- Create (via `dotnet ef migrations add`): new migration
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/BulkOnboardingRowValidatorTests.cs`

**Interfaces:**
- Consumes: `IPositionAssignmentRepository.GetActiveHoldersAsync` (Task 3).
- Produces: `RowValidationOutcome.ReportsToEmployeeId` — consumed by Task 13.

- [ ] **Step 1: Write the failing tests**

Add to `BulkOnboardingRowValidatorTests.cs`, matching its existing Moq-per-repo pattern:

```csharp
[Fact]
public async Task ValidateRowAsync_Requires_ReportingManager_Column_When_Position_Target_Is_Pooled()
{
    var positionId = Guid.NewGuid();
    var pooledTargetId = Guid.NewGuid();
    _positions.Setup(p => p.ListByLegalEntityAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Position> { new() { Id = positionId, Name = "Junior Engineer", ReportsToPositionId = pooledTargetId } });
    _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder>
        {
            new(Guid.NewGuid(), "A", "One", "a@acme.test", null),
            new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
        });

    var rawData = BuildValidRawRow() with { ["Position"] = "Junior Engineer" }; // omit reporting manager column entirely
    var mapping = BuildValidMapping(); // no "reportingManager" entry

    var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, rawData, mapping, new HashSet<string>(), CancellationToken.None);

    outcome.IsValid.Should().BeFalse();
    outcome.ErrorMessage.Should().Contain("Reporting Manager");
}

[Fact]
public async Task ValidateRowAsync_Resolves_ReportingManager_By_Email()
{
    var positionId = Guid.NewGuid();
    var pooledTargetId = Guid.NewGuid();
    var chosenManagerId = Guid.NewGuid();
    _positions.Setup(p => p.ListByLegalEntityAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Position> { new() { Id = positionId, Name = "Junior Engineer", ReportsToPositionId = pooledTargetId } });
    _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder> { new(chosenManagerId, "A", "One", "a@acme.test", null) });

    var mapping = BuildValidMapping() with { ["reportingManager"] = "Reports To" };
    var rawData = BuildValidRawRow() with { ["Position"] = "Junior Engineer", ["Reports To"] = "a@acme.test" };

    var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, rawData, mapping, new HashSet<string>(), CancellationToken.None);

    outcome.IsValid.Should().BeTrue();
    outcome.ReportsToEmployeeId.Should().Be(chosenManagerId);
}

[Fact]
public async Task ValidateRowAsync_Fails_When_ReportingManager_Email_Not_A_Current_Holder()
{
    var positionId = Guid.NewGuid();
    var pooledTargetId = Guid.NewGuid();
    _positions.Setup(p => p.ListByLegalEntityAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Position> { new() { Id = positionId, Name = "Junior Engineer", ReportsToPositionId = pooledTargetId } });
    _positionAssignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, pooledTargetId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PositionActiveHolder> { new(Guid.NewGuid(), "A", "One", "a@acme.test", null) });

    var mapping = BuildValidMapping() with { ["reportingManager"] = "Reports To" };
    var rawData = BuildValidRawRow() with { ["Position"] = "Junior Engineer", ["Reports To"] = "not-a-holder@acme.test" };

    var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, rawData, mapping, new HashSet<string>(), CancellationToken.None);

    outcome.IsValid.Should().BeFalse();
}
```

(Adjust `BuildValidRawRow()`/`BuildValidMapping()` to whatever helper names the existing test file already uses for its raw-data/mapping dictionaries — these are sketches of the shape, not exact existing helper names.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~BulkOnboardingRowValidatorTests"`
Expected: FAIL — compile error, `RowValidationOutcome` has no `ReportsToEmployeeId`.

- [ ] **Step 3: Add the entity column, config, and migration**

`BulkOnboardingBatchRow.cs`: add `public Guid? ResolvedReportsToEmployeeId { get; set; }` next to `ResolvedPositionId`, following its exact pattern.

`BulkOnboardingBatchRowConfiguration.cs`: mirror whatever configuration (if any) exists for `ResolvedPositionId` — likely none needed beyond convention, same as Task 8.

Generate the migration:
```bash
dotnet ef migrations add AddBulkOnboardingBatchRowResolvedReportsToEmployeeId --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
```
Verify it's a single plain nullable-column add on `bulk_onboarding_batch_rows`, no FK (same style as Task 1/8), and apply it: `dotnet ef database update --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api`.

- [ ] **Step 4: Add the field to `RowValidationOutcome` and the validator**

In `IBulkOnboardingRowValidator.cs`, find the `RowValidationOutcome` positional record (18 args per research findings) and append `Guid? ReportsToEmployeeId` at the end.

In `BulkOnboardingRowValidator.cs`, after the existing position resolution (research findings, lines ~79-88, name-match against `_positionRepository.ListByLegalEntityAsync`), add:

```csharp
Guid? resolvedReportsToEmployeeId = null;
if (resolvedPosition?.ReportsToPositionId is { } reportsToPositionId)
{
    var activeHolders = await _positionAssignments.GetActiveHoldersAsync(tenantId, reportsToPositionId, ct);
    if (activeHolders.Count > 1)
    {
        var reportingManagerRaw = ResolveColumnValue(mapping, rawData, "reportingManager"); // use whatever the file's existing column-lookup helper is called
        if (string.IsNullOrWhiteSpace(reportingManagerRaw))
            return Invalid($"Row references a position whose manager position has multiple current holders — a Reporting Manager column value is required.");

        var matchedHolder = activeHolders.FirstOrDefault(h =>
            string.Equals(h.WorkEmail, reportingManagerRaw.Trim(), StringComparison.OrdinalIgnoreCase));
        if (matchedHolder is null)
            return Invalid($"Reporting Manager \"{reportingManagerRaw}\" is not a current holder of this position's manager position.");

        resolvedReportsToEmployeeId = matchedHolder.EmployeeId;
    }
}
```

Then add `resolvedReportsToEmployeeId` as the new trailing argument to both the success-path `RowValidationOutcome` construction and confirm the `Invalid(...)` local function's return still compiles with the new record shape (it likely defaults every other field to `null`/`false` already — just confirm the new field defaults sanely there too).

- [ ] **Step 5: Register the field in the column-mapping suggester**

In `ColumnMappingSuggester.cs`'s `FieldAliases` dictionary, add:

```csharp
["reportingManager"] = new[] { "reporting manager", "manager", "reports to", "team lead" },
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~BulkOnboardingRowValidatorTests"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatchRow.cs src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Services/ src/ONEVO.Infrastructure/Migrations/ src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding/ tests/
git commit -m "feat: resolve bulk-onboarding Reporting Manager column by email when position target is pooled"
```

---

## Task 13: Stamp and thread the resolved manager through validate → create-drafts

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/ValidateBulkOnboardingBatch/ValidateBulkOnboardingBatchCommandHandler.cs`
- Modify: `src/ONEVO.Infrastructure/Services/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessor.cs`
- Test: existing integration tests `BulkOnboardingValidateTests.cs` and `BulkOnboardingCreateDraftsTests.cs`

**Interfaces:**
- Consumes: Task 12's `RowValidationOutcome.ReportsToEmployeeId`, Task 9's `SaveOnboardingDraftCommand.ReportsToEmployeeId`.

- [ ] **Step 1: Write the failing integration test**

Add to `BulkOnboardingCreateDraftsTests.cs`, mirroring its existing Testcontainers end-to-end style (seed a batch, seed a pooled position with two holders, upload/validate/create-drafts via the real controller pipeline):

```csharp
[Fact]
public async Task CreateDrafts_Persists_ResolvedReportsToEmployeeId_Onto_The_Draft()
{
    // Seed a pooled target position with two holders (chosenManagerId, otherHolderId).
    // Seed a batch row whose CSV mapping includes a "Reporting Manager" column value equal to chosenManagerId's work email.

    await ValidateBatchAsync(batchId); // existing helper in this file, or equivalent HTTP call
    await CreateDraftsAsync(batchId); // existing helper

    var draft = await _dbContext.OnboardingDrafts.AsNoTracking().SingleAsync(d => d.Id == /* the row's onboarding_draft_id after create-drafts */);
    draft.ReportsToEmployeeId.Should().Be(chosenManagerId);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~CreateDrafts_Persists_ResolvedReportsToEmployeeId_Onto_The_Draft"`
Expected: FAIL — `draft.ReportsToEmployeeId` is null, since nothing stamps or forwards it yet.

- [ ] **Step 3: Stamp the row in `ValidateBulkOnboardingBatchCommandHandler`**

Find the existing loop that stamps `row.ResolvedDepartmentId = outcome.DepartmentId;` / `row.ResolvedPositionId = outcome.PositionId;` and add:

```csharp
row.ResolvedReportsToEmployeeId = outcome.ReportsToEmployeeId;
```

- [ ] **Step 4: Pass it into `SaveOnboardingDraftCommand` in the batch processor**

In `BulkOnboardingBatchProcessor.ProcessDraftCreationAsync`, find the existing `new SaveOnboardingDraftCommand(...)` construction (research findings, line ~108, where `PositionId: row.ResolvedPositionId` is set) and add the corresponding argument:

```csharp
ReportsToEmployeeId: row.ResolvedReportsToEmployeeId,
```

(Match this to whichever constructor style — positional vs. `with`-expression vs. named-argument object initializer — the surrounding code actually uses; the research findings describe named-argument style for at least `PositionId`.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~CreateDrafts_Persists_ResolvedReportsToEmployeeId_Onto_The_Draft"`
Expected: PASS

- [ ] **Step 6: Run the full bulk-onboarding integration suite**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~BulkOnboarding"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/ValidateBulkOnboardingBatch/ValidateBulkOnboardingBatchCommandHandler.cs src/ONEVO.Infrastructure/Services/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessor.cs tests/
git commit -m "feat: carry resolved reporting manager from bulk validate through to draft creation"
```

---

## Task 14: Frontend models + API client method for active holders

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/people/models/onboarding-draft.model.ts`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/people/data-access/people-api.service.ts`
- Test: `Hrms--Web-application---front-end---v1/src/app/modules/people/data-access/people-api.service.spec.ts`

**Interfaces:**
- Produces: `SaveOnboardingDraftPayload.reportsToEmployeeId?: string`, `OnboardingDraftResponse.reportsToEmployeeId?: string | null`, `PeopleApiService.getActiveHolders(legalEntityId: string, positionId: string): Observable<ActiveHolder[]>`, `ActiveHolder` interface. Consumed by Task 15, 16, 17.

- [ ] **Step 1: Write the failing test**

Add to `people-api.service.spec.ts`, matching its existing `HttpTestingController` pattern:

```typescript
it('getActiveHolders requests the position active-holders endpoint', () => {
  service.getActiveHolders('legal-entity-1', 'position-1').subscribe();

  const req = httpMock.expectOne(
    `${apiBaseUrl}/org/legal-entities/legal-entity-1/positions/position-1/active-holders`
  );
  expect(req.request.method).toBe('GET');
  req.flush([]);
});
```

(Match `apiBaseUrl` to whatever base-URL constant/injection the rest of the spec file already uses.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx ng test --include='**/people-api.service.spec.ts'`
Expected: FAIL — `getActiveHolders` doesn't exist.

- [ ] **Step 3: Add the model types**

In `onboarding-draft.model.ts`, add to `SaveOnboardingDraftPayload`:
```typescript
reportsToEmployeeId?: string | null;
```
and to `OnboardingDraftResponse`:
```typescript
reportsToEmployeeId: string | null;
```
and a new exported interface:
```typescript
export interface ActiveHolder {
  employeeId: string;
  firstName: string;
  lastName: string;
  workEmail: string;
  avatarFileId: string | null;
}
```

- [ ] **Step 4: Add the API method**

In `people-api.service.ts`, add (matching the file's existing method style — likely `this.http.get<T>(...)` with the shared base-URL helper):

```typescript
getActiveHolders(legalEntityId: string, positionId: string): Observable<ActiveHolder[]> {
  return this.http.get<ActiveHolder[]>(
    `${this.baseUrl}/org/legal-entities/${legalEntityId}/positions/${positionId}/active-holders`
  );
}
```

(Adjust `this.baseUrl` to whatever the file's existing base-URL property/injection is actually called.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `npx ng test --include='**/people-api.service.spec.ts'`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/people/models/onboarding-draft.model.ts src/app/modules/people/data-access/people-api.service.ts src/app/modules/people/data-access/people-api.service.spec.ts
git commit -m "feat: add ActiveHolder model and getActiveHolders API method"
```

---

## Task 15: Shared "Reports To" picker component

**Files:**
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/people/ui/reports-to-picker/reports-to-picker.component.ts`
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/people/ui/reports-to-picker/reports-to-picker.component.spec.ts`

**Interfaces:**
- Consumes: `ActiveHolder` (Task 14).
- Produces: `<app-reports-to-picker>` with `holders: input<ActiveHolder[]>`, `selectedEmployeeId: input<string | null>`, `selectedEmployeeIdChange: output<string | null>`. Consumed by Task 16 (wizard) and Task 17 (Change Position modal).

- [ ] **Step 1: Write the failing test**

```typescript
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReportsToPickerComponent } from './reports-to-picker.component';
import { ActiveHolder } from '../../models/onboarding-draft.model';

describe('ReportsToPickerComponent', () => {
  let fixture: ComponentFixture<ReportsToPickerComponent>;
  const holders: ActiveHolder[] = [
    { employeeId: 'a', firstName: 'A', lastName: 'One', workEmail: 'a@acme.test', avatarFileId: null },
    { employeeId: 'b', firstName: 'B', lastName: 'Two', workEmail: 'b@acme.test', avatarFileId: null },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ReportsToPickerComponent] }).compileComponents();
    fixture = TestBed.createComponent(ReportsToPickerComponent);
    fixture.componentRef.setInput('holders', holders);
    fixture.detectChanges();
  });

  it('renders one option per holder', () => {
    const options = fixture.nativeElement.querySelectorAll('[data-testid="reports-to-option"]');
    expect(options.length).toBe(2);
  });

  it('emits selectedEmployeeIdChange when a holder is chosen', () => {
    const emitted: (string | null)[] = [];
    fixture.componentInstance.selectedEmployeeIdChange.subscribe((v: string | null) => emitted.push(v));

    const firstOption = fixture.nativeElement.querySelector('[data-testid="reports-to-option"]');
    firstOption.click();
    fixture.detectChanges();

    expect(emitted).toEqual(['a']);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx ng test --include='**/reports-to-picker.component.spec.ts'`
Expected: FAIL — component doesn't exist.

- [ ] **Step 3: Implement the component**

Follow this repo's standalone-component + signal-input/output convention (matching `SelectComponent`'s style referenced elsewhere in this codebase):

```typescript
import { Component, input, output } from '@angular/core';
import { ActiveHolder } from '../../models/onboarding-draft.model';

@Component({
  selector: 'app-reports-to-picker',
  standalone: true,
  template: `
    <div class="rtp-list" role="radiogroup" aria-label="Reports to">
      @for (holder of holders(); track holder.employeeId) {
        <button
          type="button"
          class="rtp-option"
          data-testid="reports-to-option"
          [class.rtp-option--selected]="holder.employeeId === selectedEmployeeId()"
          [attr.aria-pressed]="holder.employeeId === selectedEmployeeId()"
          (click)="selectedEmployeeIdChange.emit(holder.employeeId)"
        >
          {{ holder.firstName }} {{ holder.lastName }}
          <span class="rtp-option__email">{{ holder.workEmail }}</span>
        </button>
      }
    </div>
  `,
  styleUrl: './reports-to-picker.component.css'
})
export class ReportsToPickerComponent {
  holders = input<ActiveHolder[]>([]);
  selectedEmployeeId = input<string | null>(null);
  selectedEmployeeIdChange = output<string | null>();
}
```

Create an empty `reports-to-picker.component.css` alongside it (or minimal styling matching this repo's existing `bo-` / component-prefixed CSS convention seen in `bulk-onboarding.component.css`).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npx ng test --include='**/reports-to-picker.component.spec.ts'`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/people/ui/reports-to-picker/
git commit -m "feat: add ReportsToPickerComponent"
```

---

## Task 16: Onboarding wizard asks for the manager when needed

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/people/state/add-employee-wizard.store.ts`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/people/feature/add-employee-wizard/add-employee-wizard.component.ts`
- Test: `add-employee-wizard.store.spec.ts` and `add-employee-wizard.component.spec.ts`

**Interfaces:**
- Consumes: `PeopleApiService.getActiveHolders` (Task 14), `ReportsToPickerComponent` (Task 15).

- [ ] **Step 1: Write the failing store test**

Add to `add-employee-wizard.store.spec.ts`, matching its existing NgRx signal-store test setup:

```typescript
it('setPosition loads active holders and needsReportsToPick reflects holder count', async () => {
  peopleApiSpy.getActiveHolders.and.returnValue(of([
    { employeeId: 'a', firstName: 'A', lastName: 'One', workEmail: 'a@acme.test', avatarFileId: null },
    { employeeId: 'b', firstName: 'B', lastName: 'Two', workEmail: 'b@acme.test', avatarFileId: null },
  ]));

  await store.setPosition('position-1', 'reports-to-position-1');

  expect(store.needsReportsToPick()).toBe(true);
  expect(store.activeHolders().length).toBe(2);
});

it('needsReportsToPick is false when the target position has a single holder', async () => {
  peopleApiSpy.getActiveHolders.and.returnValue(of([
    { employeeId: 'a', firstName: 'A', lastName: 'One', workEmail: 'a@acme.test', avatarFileId: null },
  ]));

  await store.setPosition('position-1', 'reports-to-position-1');

  expect(store.needsReportsToPick()).toBe(false);
});
```

(Adjust `setPosition`'s exact signature — the store may already resolve the position's `ReportsToPositionId` server-side via the draft-save response rather than accepting it as a second argument; check `add-employee-wizard.store.ts`'s actual `setPosition` before writing this, and if the reports-to-position-id isn't available client-side at select time, call `getActiveHolders` keyed on the *position itself* and have the backend endpoint resolve `ReportsToPositionId` internally instead — in that case, Task 7's endpoint signature and this store call both take just `positionId`, not the target's id. Confirm which is actually true against Task 7's implementation before finalizing this task's shape.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx ng test --include='**/add-employee-wizard.store.spec.ts'`
Expected: FAIL — `needsReportsToPick`/`activeHolders`/updated `setPosition` don't exist yet.

- [ ] **Step 3: Add the state to the store**

In `add-employee-wizard.store.ts`, add signals for `activeHolders`, `selectedReportsToEmployeeId`, and a computed `needsReportsToPick = computed(() => activeHolders().length > 1)`. Extend `setPosition` to call `peopleApi.getActiveHolders(...)` and populate `activeHolders`, resetting `selectedReportsToEmployeeId` to `null` on every position change (mirroring how the store already resets template selection on position change, per the research findings).

Add a `setReportsToEmployeeId(employeeId: string | null)` method updating the signal, and thread `selectedReportsToEmployeeId()` into the payload built by `saveDraft()` as `reportsToEmployeeId`.

- [ ] **Step 4: Run the store test to verify it passes**

Run: `npx ng test --include='**/add-employee-wizard.store.spec.ts'`
Expected: PASS

- [ ] **Step 5: Write the failing component test**

Add to `add-employee-wizard.component.spec.ts`:

```typescript
it('blocks save when a reports-to pick is needed but not made', () => {
  store.needsReportsToPick.set(true); // or however the spec's store test double exposes this
  store.selectedReportsToEmployeeId.set(null);
  fixture.detectChanges();

  component.onSaveDraft();

  expect(saveDraftSpy).not.toHaveBeenCalled();
});
```

- [ ] **Step 6: Run the test to verify it fails**

Run: `npx ng test --include='**/add-employee-wizard.component.spec.ts'`
Expected: FAIL.

- [ ] **Step 7: Wire the gating and render the picker**

In `add-employee-wizard.component.ts`, extend the existing `this.form.invalid` gate on `onSaveDraft()`/`onFinalize()` (research findings) with `|| (this.store.needsReportsToPick() && !this.store.selectedReportsToEmployeeId())`.

In the component's template, render `<app-reports-to-picker>` conditionally when `store.needsReportsToPick()` is true, right after the position field, bound to `store.activeHolders()` / `store.selectedReportsToEmployeeId()` / `(selectedEmployeeIdChange)="store.setReportsToEmployeeId($event)"`.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `npx ng test --include='**/add-employee-wizard.component.spec.ts'`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/app/modules/people/state/add-employee-wizard.store.ts src/app/modules/people/feature/add-employee-wizard/
git commit -m "feat: ask for reporting manager in onboarding wizard when position target is pooled"
```

---

## Task 17: Change Position modal asks for the manager when needed

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/people/ui/change-position-modal/change-position-modal.component.ts`
- Test: `change-position-modal.component.spec.ts`

**Interfaces:**
- Consumes: `PeopleApiService.getActiveHolders` (Task 14), `ReportsToPickerComponent` (Task 15).

- [ ] **Step 1: Write the failing test**

Following the existing spec file's signal-input harness pattern:

```typescript
it('disables the submit action until a manager is picked when the position target is pooled', () => {
  peopleApiSpy.getActiveHolders.and.returnValue(of([
    { employeeId: 'a', firstName: 'A', lastName: 'One', workEmail: 'a@acme.test', avatarFileId: null },
    { employeeId: 'b', firstName: 'B', lastName: 'Two', workEmail: 'b@acme.test', avatarFileId: null },
  ]));
  fixture.componentRef.setInput('open', true);
  selectPosition(fixture, 'position-with-pooled-target');
  fixture.detectChanges();

  const submitButton = fixture.nativeElement.querySelector('[data-testid="change-position-submit"]');
  expect(submitButton.disabled).toBe(true);
});
```

(`selectPosition` here is a placeholder for whatever helper the existing spec file already uses to drive the raw `<select>` — check the file first.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx ng test --include='**/change-position-modal.component.spec.ts'`
Expected: FAIL.

- [ ] **Step 3: Add the state and picker to the modal**

Mirror the existing `selectedPositionRequiresApproval` computed (per research findings) with a new `selectedPositionRequiresManagerPick` computed, driven by a signal populated from `peopleApi.getActiveHolders(...)` triggered by the same effect that already watches position selection. Add `<app-reports-to-picker>` to the inline template, rendered conditionally on `selectedPositionRequiresManagerPick()`, and extend the submit button's existing `[disabled]` binding with `|| (selectedPositionRequiresManagerPick() && !selectedReportsToEmployeeId())`.

Thread the selected value into whatever payload the modal emits on submit (`ChangeEmployeePositionCommand`'s frontend equivalent) as `reportsToEmployeeId`.

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx ng test --include='**/change-position-modal.component.spec.ts'`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/people/ui/change-position-modal/
git commit -m "feat: ask for reporting manager in Change Position modal when target is pooled"
```

---

## Task 18: Bulk onboarding — "Reporting Manager" mapping field

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/people/models/bulk-onboarding.model.ts`
- Test: `bulk-onboarding.component.spec.ts`

**Interfaces:**
- Consumes: nothing new — purely a mapping-UI registration, since (per research) the mapping step's field list is data-driven off `BulkOnboardingSystemField`.

- [ ] **Step 1: Write the failing test**

Add to `bulk-onboarding.component.spec.ts`:

```typescript
it('renders a Reporting Manager row in the column-mapping step', () => {
  component.goToStep('map-columns');
  fixture.detectChanges();

  const labels = Array.from(fixture.nativeElement.querySelectorAll('.bo-map-row__label'))
    .map((el: HTMLElement) => el.textContent?.trim());
  expect(labels.some(l => l?.includes('Reporting Manager'))).toBe(true);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx ng test --include='**/bulk-onboarding.component.spec.ts'`
Expected: FAIL.

- [ ] **Step 3: Add the field**

In `bulk-onboarding.model.ts`, add `'reportingManager'` to the `BulkOnboardingSystemField` union type and a corresponding entry in `BULK_ONBOARDING_SYSTEM_FIELD_LABELS`:

```typescript
export type BulkOnboardingSystemField =
  | 'firstName' | 'lastName' | 'workEmail' | 'startDate' | 'employmentType'
  | 'workMode' | 'department' | 'position' | 'checklistTemplate' | 'employeeNumber'
  | 'reportingManager';

export const BULK_ONBOARDING_SYSTEM_FIELD_LABELS: Record<BulkOnboardingSystemField, string> = {
  // ...existing entries unchanged...
  reportingManager: 'Reporting Manager',
};
```

Do **not** add `'reportingManager'` to `REQUIRED_MAPPING_FIELDS` in `bulk-onboarding.component.ts` — it's conditionally required per-row server-side (Task 12), not universally required, matching the design's central point that this field is asked only when ambiguous.

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx ng test --include='**/bulk-onboarding.component.spec.ts'`
Expected: PASS

- [ ] **Step 5: Update the validate-step copy for clarity**

In `bulk-onboarding.component.html`'s validate-step section, no structural change is needed (the existing per-row error table already renders whatever `errorMessage` the backend sends, including Task 12's new "Reporting Manager" error text) — confirm this by re-reading that template section, and skip any edit if it already generically renders row errors.

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/people/models/bulk-onboarding.model.ts src/app/modules/people/feature/bulk-onboarding/
git commit -m "feat: add Reporting Manager field to bulk onboarding column mapping"
```

---

## Self-Review Notes

- **Spec coverage**: §4 (data model) → Tasks 1, 8, 12. §5 (resolution algorithm) → Task 4. §6 (where asked) → Tasks 16, 17, 18 (+ 9/11/12 backend validation). §7 (API surface) → Task 7 (corrected route/permission per research, documented in Global Constraints). §8 (validation rules) → Tasks 9, 11, 12. §9 open items (wiring mechanism, transaction boundary, update-after-creation, backfill) — wiring mechanism resolved as repository-level (Task 5) per the "less likely to be missed by a future call site" guidance; transaction boundary resolved as "after the triggering write commits" (Tasks 5/6); update-after-creation and backfill remain explicitly out of scope, not silently dropped — no task claims to solve either.
- **Corrections applied during planning** (differences from the original design doc, based on verified current code): the API route/permission family (§7) was corrected from a guessed `employees:read`/`api/v1/onboarding/...` shape to the real `org:read`/`api/v1/org/legal-entities/.../positions/...` convention. The persistence point for `reportsToEmployeeId` was corrected from "passed at finalize time" to "persisted on `OnboardingDraft` at save time, read back at finalize" (Tasks 9-10), since Save and Finalize are separate requests and the original design under-specified this.
- **Type consistency**: `ReportsToEmployeeId` (PascalCase, backend C#) / `reportsToEmployeeId` (camelCase, frontend TS and JSON wire) used consistently across all 18 tasks. `PositionActiveHolder` (repository-layer model, Task 3) vs. `ActiveHolderViewModel` (API contract, Task 7) vs. `ActiveHolder` (frontend model, Task 14) are three distinct types by design (matching this repo's existing Domain/Contracts/frontend-model layering), not a naming inconsistency — each task's Interfaces block states which one it produces/consumes.
