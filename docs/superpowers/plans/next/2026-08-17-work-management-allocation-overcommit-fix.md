# Work Management — Allocation Overcommit & Insufficient-Allocation JSON Casing Fix

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or
> superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox
> (`- [ ]`) syntax for tracking. This plan was written after live root-cause investigation (browser
> repro against the running dev servers) — the diagnosis below is confirmed, not speculative.

**Goal:** Fix three independently-confirmed bugs surfaced while manually testing the Task Foundation
feature against the `dapi` demo tenant: (1) the dapi demo Objective trees are internally
over-committed — sibling Objectives' `AllocatedHours` sum to far more than their parent's, driving
`IObjectiveAllocationSlackCalculator` deeply negative (confirmed: **-3930h** on the HWPORTAL project
root) and blocking task creation/allocation-extension approval anywhere it's hit; (2) the
"insufficient allocation" 409 error body is serialized with default PascalCase casing in three
places, but the frontend's `tryParseInsufficientAllocation` expects camelCase, so the intended
friendly "Not enough allocation... Request more allocation" UI never fires — the user just sees raw
JSON text; (3) **approving any `extend_allocation` change request crashes with a 500** —
`ApproveObjectiveChangeRequestCommandHandler` throws an EF Core `InvalidOperationException`
("cannot be tracked because another instance with the same key... is already being tracked") every
single time, in any tenant, not just this demo data — this is a universal bug, not a seed-data
artifact, and it blocks this plan's own manual-verification step, so it must be fixed in this same
pass.

**Scope discipline — read before touching anything:**
- **`ObjectiveParentConstraintChecker` is NOT to be changed.** Its own doc comment states this is
  deliberate: a child's hours are checked against the parent's *total*, not remaining headroom after
  siblings — "deliberately simple, matching phase1-table-inventory.md's existing warning-only
  treatment of hours elsewhere." This is intentional, pre-existing, documented design. The bug here
  is entirely that the **demo seed data** produces an Objective tree inconsistent with what a
  well-formed tree should look like — not that the production business rule is wrong. Do not loosen
  or tighten this checker as part of this plan.
- Work Management module only (backend repo). No frontend changes needed — the frontend's
  `tryParseInsufficientAllocation` (`task-board.store.ts:22-38`) already correctly `JSON.parse`s the
  nested `detail` string and checks for camelCase keys; it's already written correctly for the
  contract the backend *should* be emitting. Only the backend's casing needs to change to match.

## Background — confirmed via live repro, not guesswork

Reproduced by logging into `https://dapi.localhost:4200` as `dapiyshanth1908@gmail.com` /
`Password123!` (dabi, tenant owner), opening HWPORTAL's root Objective Board tab
(`33bbc8ca-3731-640f-bd1e-6f38ed9e55b9`), and submitting Create Task with any `Estimated Hours` value:

```
POST /api/v1/work/objectives/33bbc8ca-3731-640f-bd1e-6f38ed9e55b9/tasks → 409
{"AvailableSlackHours":-3930.00,"SuggestedAction":"extend_allocation"}
```

Submitting the same task **without** an Estimated Hours value succeeds (201) — proving the block is
specifically `CreateTaskCommandHandler`'s slack check (`CreateTaskCommandHandler.cs:69-75`), not a
validation-field problem.

**Root cause of the negative slack:** `ComputeChildHours(decimal parentHours, int siblingIndex)` in
`src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Objectives.cs:218-227`
assigns each child a percentage of the parent's hours — `0.70 - (siblingIndex * 0.05)`, floored at
`0.30` — **computed independently per sibling, with no normalization across siblings**. For any
Objective with 2+ children, the first two alone already claim 70%+65% = 135% of the parent. HWPORTAL's
root has 4 children (Device Connectivity Framework, Portal Dashboard And Monitoring, Testing and
deployment, Marketing): 70+65+60+55 = **250%** of its 2600h AllocatedHours. This same shape recurs at
every branching point in every one of the 5 project trees — it is not specific to HWPORTAL or to
roots, it hits any non-leaf Objective with 2 or more children.

This predates the Task Foundation work entirely — `ComputeChildHours` was written for the original
2026-08-12 Objective-tree seeding plan, whose own test
(`SeedAsync_EveryObjective_SatisfiesParentDateAndHoursContainment`) only asserts each child against
its *immediate* parent individually — it never summed siblings, so the bug was invisible until
`IObjectiveAllocationSlackCalculator` (built for Task Foundation, sums *all* active children) started
checking the real aggregate.

**This also explains the `change-requests/.../approve` 409s** seen earlier in manual testing: the
conditional-approval logic for `extend_allocation` requires the *approver's own* Objective to have
enough slack (see design memory on Allocation-extend requests). Since every seeded Objective's
`ReportingManagerId` is `dabi`, and dabi only owns each project's root, **every approval in this demo
tenant is gated on that project's root having slack** — and the roots are the worst-overcommitted
nodes tree-wide (they have the most children). Fixing the overcommit is what unblocks the Approvals
demo too, not just direct task creation.

**Root cause of the JSON casing bug:** three call sites build the 409 body via a bare
`System.Text.Json.JsonSerializer.Serialize(new InsufficientAllocationResponse(slack))` with no
`JsonSerializerOptions` — this uses `System.Text.Json`'s absolute default (PascalCase, matching the
C# record's property names exactly), bypassing whatever camelCase convention the rest of the API uses
for its normal `Ok(...)`/`Created(...)` responses. The three sites:
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs:73-74`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs:49-50`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs:81-82`

Frontend confirmation (`src/app/modules/work/state/task-board.store.ts:22-38` in the frontend repo,
already correct, no change needed): `tryParseInsufficientAllocation` does
`JSON.parse(body.detail)` then checks `parsed.availableSlackHours` and
`parsed?.suggestedAction === 'extend_allocation'` — both camelCase. Since the backend sends
`AvailableSlackHours`/`SuggestedAction`, both checks fail, `tryParseInsufficientAllocation` returns
`null`, and the code falls through to the generic error path, which is exactly the raw-JSON-dump
behavior observed in the browser.

**Root cause of the approve-500 (confirmed via full server-side stack trace, not inferred):**

```
System.InvalidOperationException: The instance of entity type 'Objective' cannot be tracked because
another instance with the same key value for {'Id'} is already being tracked.
   at ...EfObjectiveRepository.Update(Objective objective) ...EfObjectiveRepository.cs:line 70
   at ...ApproveObjectiveChangeRequestCommandHandler...Handle...ApproveObjectiveChangeRequestCommandHandler.cs:line 164
```

`EfObjectiveRepository.GetByIdForTenantAsync` (`EfObjectiveRepository.cs:33-38`) uses
`.AsNoTracking()` — so `objective` (the Objective being extended, fetched at
`ApproveObjectiveChangeRequestCommandHandler.cs:66`) is a **detached** instance. But
`GetTrackedActiveDirectChildrenAsync` (`EfObjectiveRepository.cs:56-61`) has no `.AsNoTracking()` —
it's a **tracking** query. The `ExtendAllocation` branch's slack check
(`ApproveObjectiveChangeRequestCommandHandler.cs:141`) calls it on the *approver's own objective*
(line 137, resolved as `objective.ParentObjectiveId`) to sum that objective's children's hours — and
since `objective` is, by definition, one of its own parent's direct children, this pulls a **second,
separately-tracked instance of the exact same row** into the DbContext's change tracker. When the
handler later calls `_objectives.Update(objective)` (line 164) on the original *detached* instance,
EF's identity map already holds that other tracked instance for the same key, and the attach fails.

**This is not demo-data-specific — it is structurally guaranteed to happen on every single
`extend_allocation` approval, in any tenant.** The objective being approved is *always* a direct
child of "the approver's own objective" (that's literally how `objective.ParentObjectiveId` is used
to resolve the approver's objective at line 137), so the slack-check's
`GetTrackedActiveDirectChildrenAsync` call will *always* re-fetch the same row as a second tracked
instance. Confirm this is why the existing unit tests never caught it:
`tests/ONEVO.Tests.Unit/Features/WorkManagement/ApproveObjectiveChangeRequestCommandHandlerTests.cs`
mocks `IObjectiveRepository` entirely (Moq) — a mock has no change tracker, so the exact mechanism
that crashes in production is invisible to these tests. A regression test for this needs a real
`SqliteTestApplicationDbContext` + real `EfObjectiveRepository`, not mocks (see Task 3, Step 1).

The interface docs for `IObjectiveRepository.GetTrackedActiveDirectChildrenAsync`
(`IObjectiveRepository.cs:29-36`) already reference a `GetTrackedByIdForTenantAsync` method by name
in a comment ("same AsNoTracking-vs-tracked distinction as GetTrackedByIdForTenantAsync") — but no
such method actually exists on `IObjectiveRepository` today. It **does** already exist on
`IWorkTaskRepository` (used by `EditTaskCommandHandler.cs:37`) — this is an established,
already-idiomatic pattern in this codebase, just missing for Objectives. Adding it is the fix.

## Global Constraints

- Dev/Test-only seeder code — keep the existing `!_environment.IsDevelopment() &&
  !_environment.IsEnvironment("Test")` guards untouched.
- `ComputeChildHours` is `private static` inside the `partial class WorkManagementDapiDemoSeeder` — it
  is defined once in `.Objectives.cs` and called from both `.Objectives.cs`'s `SeedObjectiveNodeAsync`
  and `.Tasks.cs`'s `EnumerateLeaves`. Fixing the one definition fixes both call sites; both call
  sites' loops need a small update to pass sibling count (see Task 1).
- Every `AllocationExtends` spec (`WorkManagementDapiDemoData.cs`) requests additional hours on a
  specific non-root Objective, approved by dabi via that Objective's project root. The new formula's
  reserved headroom at the root level must comfortably exceed every project's requested amount (all
  are 30-50h; verify against the new ~20% reserve in Task 4 — for the smallest root, HWPORTAL at
  2600h, 20% = 520h, comfortably above its 35h request).
- The seeder is idempotent by design (`if (existing is null) { add }` — never updates). Changing the
  formula in code does **not** retroactively fix rows already seeded into a running dev database — see
  Task 5.

---

### Task 1: Normalize `ComputeChildHours` so siblings never overcommit their parent

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Objectives.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs` (call
  site only — `EnumerateLeaves`)
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs`

- [ ] **Step 1: Write the failing test — the invariant the original seeder never checked**

Append to `WorkManagementDapiDemoSeederTests.cs`:

```csharp
    [Fact]
    public async Task SeedAsync_EveryObjective_ChildrenNeverCollectivelyExceedParentAllocation()
    {
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();

        await WorkManagementDapiDemoSeeder.SeedAsync(_db, tenantContext, passwordHasher, CancellationToken.None);
        await _db.SaveChangesAsync();

        var all = await _db.Objectives
            .Where(o => o.TenantId == Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb") && o.IsActive)
            .ToListAsync();
        var byParent = all.Where(o => o.ParentObjectiveId.HasValue)
            .GroupBy(o => o.ParentObjectiveId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.AllocatedHours));
        var byId = all.ToDictionary(o => o.Id);

        foreach (var (parentId, childSum) in byParent)
        {
            var parent = byId[parentId];
            Assert.True(childSum <= parent.AllocatedHours,
                $"{parent.Title}: children sum to {childSum}h but parent only has {parent.AllocatedHours}h");
        }
    }

    [Theory]
    [InlineData("epos", "E-pos_System", 40)]
    [InlineData("evtix", "Event management ticketing", 30)]
    [InlineData("onexso", "Onexso - HR and Work Management System", 50)]
    [InlineData("watercraft", "Watercraft", 45)]
    [InlineData("hwportal", "The Hardware integration portal", 35)]
    public async Task SeedAsync_ProjectRoot_RetainsEnoughSlackForItsAllocationExtendDemo(
        string projectKey, string rootTitle, decimal requiredSlack)
    {
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();

        await WorkManagementDapiDemoSeeder.SeedAsync(_db, tenantContext, passwordHasher, CancellationToken.None);
        await _db.SaveChangesAsync();

        var root = await _db.Objectives.SingleAsync(o => o.Title == rootTitle && o.IsDefault);
        var childSum = await _db.Objectives
            .Where(o => o.ParentObjectiveId == root.Id && o.IsActive)
            .SumAsync(o => o.AllocatedHours);

        Assert.True(root.AllocatedHours - childSum >= requiredSlack,
            $"{rootTitle}: only {root.AllocatedHours - childSum}h slack, needs >= {requiredSlack}h for its AllocationExtends demo");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementDapiDemoSeederTests`
Expected: `ChildrenNeverCollectivelyExceedParentAllocation` FAILs immediately (HWPORTAL root will show
~6530h of children against 2600h capacity, matching the -3930 slack observed live).

- [ ] **Step 3: Rewrite `ComputeChildHours` to normalize across siblings**

In `WorkManagementDapiDemoSeeder.Objectives.cs`, replace the constants block and `ComputeChildHours`:

```csharp
    private const int DateInsetBaseDays = 4;
    private const decimal HoursRatioStart = 0.70m;
    private const decimal HoursRatioStep = 0.05m;
    private const decimal HoursRatioFloor = 0.30m;
    private const decimal MinimumAllocatedHours = 10m;
    private const decimal ChildAllocationCeiling = 0.80m; // siblings collectively never exceed 80% of the parent
```

```csharp
    private static decimal ComputeChildHours(decimal parentHours, int siblingIndex, int siblingCount)
    {
        // Same front-loaded "shape" as before (earlier siblings get a bigger raw share), but now
        // normalized so the whole sibling group sums to ChildAllocationCeiling of the parent,
        // regardless of how many children there are - this is what the old per-sibling-only formula
        // never did, which is the root cause of the overcommit bug.
        var weights = Enumerable.Range(0, siblingCount)
            .Select(i => Math.Max(HoursRatioFloor, HoursRatioStart - (i * HoursRatioStep)))
            .ToArray();
        var totalWeight = weights.Sum();
        var share = weights[siblingIndex] / totalWeight;

        var hours = Math.Round(parentHours * ChildAllocationCeiling * share, 0, MidpointRounding.AwayFromZero);
        return hours < MinimumAllocatedHours ? MinimumAllocatedHours : hours;
    }
```

Add `using System.Linq;` if not already present in this file.

- [ ] **Step 4: Update both call sites to pass sibling count**

In `WorkManagementDapiDemoSeeder.Objectives.cs`'s `SeedObjectiveNodeAsync`, the loop building children:

```csharp
        for (var i = 0; i < node.Children.Length; i++)
        {
            var child = node.Children[i];
            var (childStart, childEnd) = ComputeChildDates(start, end, i);
            var childHours = ComputeChildHours(allocatedHours, i, node.Children.Length);
            // ... unchanged below
```

In `WorkManagementDapiDemoSeeder.Tasks.cs`'s `EnumerateLeaves`:

```csharp
        for (var i = 0; i < node.Children.Length; i++)
        {
            var child = node.Children[i];
            var childHours = ComputeChildHours(allocatedHours, i, node.Children.Length);
            // ... unchanged below
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementDapiDemoSeederTests`
Expected: all PASS, including the two new tests and every pre-existing test in this file (in
particular `SeedAsync_EveryObjective_SatisfiesParentDateAndHoursContainment`, which must still pass —
the per-child-vs-parent containment property is preserved, this fix only adds the missing sum
constraint on top of it).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Objectives.cs src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs
git commit -m "fix(seed): normalize sibling Objective hours so they never collectively overcommit their parent"
```

---

### Task 2: Fix `InsufficientAllocationResponse` JSON casing at all three call sites

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs`
  (add a shared serialization helper near `InsufficientAllocationResponse`)
- Modify: `CreateTaskCommandHandler.cs`, `EditTaskCommandHandler.cs`,
  `ApproveTaskCreationRequestCommandHandler.cs`
- Test: add unit tests asserting camelCase output (exact file/location left to the implementer —
  match this repo's existing convention for handler-level tests under
  `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/Commands/`)

- [ ] **Step 1: Write the failing test(s)**

For each of the three handlers (or a shared parametrized test if their test fixtures allow it),
assert that when the slack check fails, the `Result.Error` string — once parsed as JSON — has a
`availableSlackHours` key (lowercase `a`), not `AvailableSlackHours`. Example shape (adapt to each
handler's existing test fixture/mocking pattern found in
`tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandlerTests.cs`
or equivalent):

```csharp
    [Fact]
    public async Task Handle_InsufficientAllocation_ReturnsCamelCaseErrorBody()
    {
        // ... arrange so the slack check fails (existing insufficient-allocation test in this file
        // likely already does this - reuse its arrange, add this assertion) ...

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("\"availableSlackHours\"", result.Error);
        Assert.DoesNotContain("\"AvailableSlackHours\"", result.Error);
    }
```

Check each of the three handler test files for whether an "insufficient allocation" test already
exists (there very likely is one, since the 409 path itself is presumably already covered) — if so,
extend that existing test with the casing assertion rather than duplicating the arrange/act.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~InsufficientAllocation|FullyQualifiedName~CamelCase"`
(adjust filter to match whatever the actual test names end up being)
Expected: FAIL — current output is PascalCase.

- [ ] **Step 3: Add a shared camelCase serialization helper**

In `WorkTaskResponse.cs` (or a small new static class alongside it — implementer's judgment on the
cleanest placement within `ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses`), add:

```csharp
public static class InsufficientAllocationResponseJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static string Serialize(InsufficientAllocationResponse response)
        => System.Text.Json.JsonSerializer.Serialize(response, Options);
}
```

- [ ] **Step 4: Replace all three call sites**

In each of `CreateTaskCommandHandler.cs`, `EditTaskCommandHandler.cs`,
`ApproveTaskCreationRequestCommandHandler.cs`, replace:

```csharp
System.Text.Json.JsonSerializer.Serialize(new InsufficientAllocationResponse(slack))
```

(or the unqualified `JsonSerializer.Serialize(...)` variant, same thing) with:

```csharp
InsufficientAllocationResponseJson.Serialize(new InsufficientAllocationResponse(slack))
```

Add the appropriate `using` for the DTOs namespace if not already present in each file (all three
already reference `InsufficientAllocationResponse`, so the namespace is already imported — just the
new type name needs to resolve).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement.Tasks`
Expected: all PASS, including the new camelCase assertions and every pre-existing test for these
three handlers (behavior otherwise unchanged — only the casing of the error payload changes).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/Commands/
git commit -m "fix(work): serialize InsufficientAllocationResponse as camelCase so the frontend's allocation-hint UI actually triggers"
```

---

### Task 3: Fix the EF Core duplicate-tracking crash on `extend_allocation` approval

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs`
- Test: new integration-style test using the real SQLite DbContext (mocks cannot catch this class of
  bug — see Background)

- [ ] **Step 1: Write the failing integration test**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/ApproveObjectiveChangeRequestCommandHandlerIntegrationTests.cs`,
following the same `SqliteTestApplicationDbContext` harness pattern used in
`WorkManagementDapiDemoSeederTests.cs` (or wherever this repo's other real-DbContext integration
tests live — check `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/` for the exact setup
helpers/fakes to reuse, e.g. `FakeWritableTenantContext`). Seed: a tenant, a parent Objective (the
"approver's own" one, owned by the approving employee, with enough `AllocatedHours` and no other
children), a child Objective of it (the one being extended, owned by someone else, with
`ReportingManagerId` set to the approver's employee id), and a pending `ObjectiveChangeRequest` of
type `extend_allocation` targeting the child. Use the real `EfObjectiveRepository`,
`EfObjectiveChangeRequestRepository`, `ObjectiveAllocationSlackCalculator`, and
`ApproveObjectiveChangeRequestCommandHandler` wired together against the real DbContext — no mocks
for the repository layer. Then:

```csharp
    [Fact]
    public async Task Handle_ExtendAllocation_DoesNotThrowDuplicateTrackingException()
    {
        // Arrange: real SqliteTestApplicationDbContext, real EfObjectiveRepository, a parent
        // Objective owned by the approver with slack, one child Objective (the one being extended)
        // that is a direct child of the parent, and a pending extend_allocation
        // ObjectiveChangeRequest on that child routed to the approver. This exact shape - the
        // extended objective being a direct child of the approver's own objective - is what
        // triggers the duplicate-tracking bug in production; do not simplify it away.

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(requestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updatedChild = await db.Objectives.AsNoTracking().SingleAsync(o => o.Id == childObjectiveId);
        Assert.Equal(expectedNewAllocatedHours, updatedChild.AllocatedHours);
    }
```

- [ ] **Step 2: Run to verify it fails with the exact production exception**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ApproveObjectiveChangeRequestCommandHandlerIntegrationTests`
Expected: FAIL with `System.InvalidOperationException: The instance of entity type 'Objective' cannot
be tracked...` — the same exception confirmed live in production. If it does *not* fail this way,
the test arrangement doesn't match the real bug shape — fix the arrangement, don't proceed.

- [ ] **Step 3: Add `GetTrackedByIdForTenantAsync` to `IObjectiveRepository`**

In `IObjectiveRepository.cs`, add (mirroring the existing `GetTrackedDefaultByProjectIdAsync` doc
style, and matching `IWorkTaskRepository`'s already-existing method of the same name):

```csharp
    /// <summary>
    /// Same lookup as <see cref="GetByIdForTenantAsync"/>, but returns the entity tracked by the
    /// DbContext's change tracker instead of AsNoTracking. Use on write paths that later call
    /// <see cref="Update"/> or mutate the entity directly - tracking it from the start lets EF's
    /// identity map correctly deduplicate against any other tracked query that touches the same
    /// row later in the same request (see ApproveObjectiveChangeRequestCommandHandler's
    /// extend_allocation branch for why this matters - GetTrackedActiveDirectChildrenAsync can
    /// re-fetch this same row as part of a sibling-sum check).
    /// </summary>
    Task<Objective?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
```

- [ ] **Step 4: Implement it in `EfObjectiveRepository`**

```csharp
    public async Task<Objective?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Objectives
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == id, ct);
    }
```

- [ ] **Step 5: Use it in the handler**

In `ApproveObjectiveChangeRequestCommandHandler.cs:66`, change:

```csharp
        var objective = await _objectives.GetByIdForTenantAsync(tenantId, changeRequest.ObjectiveId, ct);
```

to:

```csharp
        var objective = await _objectives.GetTrackedByIdForTenantAsync(tenantId, changeRequest.ObjectiveId, ct);
```

**Do not remove the later `_objectives.Update(objective)` call at line 164.** Once `objective` is
tracked from the start, EF's identity map will correctly resolve any later
`GetTrackedActiveDirectChildrenAsync` hit on the same row to this same tracked instance (no
duplicate), so `Update()` on it is safe — just slightly more than strictly necessary (marks every
property Modified rather than only the changed ones, per the interface's own efficiency note on
`GetTrackedDefaultByProjectIdAsync`). Leaving it in place keeps the existing mocked unit tests in
`ApproveObjectiveChangeRequestCommandHandlerTests.cs` green without modification, since those assert
`Update()` was called.

- [ ] **Step 6: Run both test suites to verify**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ApproveObjectiveChangeRequestCommandHandler`
Expected: the new integration test PASSes (no more `InvalidOperationException`), and all existing
mocked tests in `ApproveObjectiveChangeRequestCommandHandlerTests.cs` still PASS unchanged.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/ApproveObjectiveChangeRequestCommandHandlerIntegrationTests.cs
git commit -m "fix(work): fetch the objective being extended as tracked, fixing a 500 on every extend_allocation approval"
```

---

### Task 4: Reset the dev database so the fixed seeder actually re-runs clean

**This is a manual operational step, not code — flag it clearly to the user rather than scripting it
silently, since dropping a database is destructive.**

The seeder is idempotent by design (skip-if-exists via deterministic Guids) — it will **not** update
the already-seeded, already-overcommitted Objective rows sitting in the current dev database just
because the formula changed in code. The fixed seeder only produces correct data on a database where
these rows don't exist yet.

- [ ] **Step 1: Stop the running backend dev server** (the one bound to port 7229) before touching the
  database, so nothing writes to it mid-reset.
- [ ] **Step 2: Drop and recreate the local dev database**, then let EF Core migrations + all
  `IHostedService` seeders (including the now-fixed `WorkManagementDapiDemoSeeder`) run fresh on next
  startup. Use whatever this repo's existing documented dev-reset procedure is (check
  `docs/superpowers/` for a "reset dev db" or "local setup" doc, or `dotnet ef database drop` /
  equivalent against the connection string in `appsettings.Development.json` if no doc exists).
- [ ] **Step 3: Tell the user explicitly, before running this**, that a full reset also removes any
  manually-created objects outside the deterministic dapi-demo dataset — e.g., the "hi" test Objective
  created by hand during earlier manual testing. Get their confirmation this is acceptable (it's dev
  data, but let them decide, don't assume).
- [ ] **Step 4: Restart the backend**, confirm the startup logs show
  `"Work Management dapi demo dataset seeded (22 employees, 5 projects)."` with no errors.

---

### Task 5: Manual verification — repeat the original repro, confirm it's fixed

- [ ] Log into `https://dapi.localhost:4200` as `dapiyshanth1908@gmail.com` / `Password123!`.
- [ ] Open HWPORTAL's project root Objective → Board tab → Create task, fill in a small positive
  Estimated Hours value (e.g., 5), submit. Expect **201 Created**, not 409.
- [ ] Repeat for the other 4 projects' roots as a spot-check.
- [ ] Open the Approvals tab for at least 2 of the 5 projects, attempt to approve one of the seeded
  `extend_allocation` requests. Expect success, not 409 (unless the specific business scenario is
  intentionally supposed to demonstrate the conditional-approval-blocks case — cross-check against
  [[project_hrms_task_foundation_build]]'s notes on this design before treating any remaining 409 here
  as a bug).
- [ ] Trigger one more insufficient-allocation case on purpose (e.g., an enormous Estimated Hours
  value on a task) and confirm the UI now shows the friendly "Not enough allocation (Nh available).
  Request more allocation" hint — not raw JSON text.

---

### Task 6: Full regression run before calling this done

- [ ] Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` (broad —
  this plan touches core Objective-tree generation, worth checking nothing else in Work Management
  regressed) and confirm all green.
- [ ] Run the full unit suite once more if time allows: `dotnet test tests/ONEVO.Tests.Unit`.
