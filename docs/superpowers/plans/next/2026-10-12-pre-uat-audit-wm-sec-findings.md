# Pre-UAT Audit — WM/SEC Findings Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to continue this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve the six findings assigned from the "ONEXSO Pre-UAT Audit Triage" (2026-10-11) — WM-01, WM-02, WM-03, WM-04, WM-05, SEC-13 — fixing the one real bug (WM-02), verifying the two confirmed-by-design items (WM-03, WM-05), documenting/annotating the one dead-field item (WM-01), closing a narrow content gap in the existing cross-project approvals aggregator (WM-04), and shipping non-destructive upload hardening for SEC-13 while leaving real malware scanning explicitly BLOCKED.

**Architecture:** No new subsystems. WM-02 ports an existing optimistic-concurrency idiom (`IsConcurrencyToken()` + repo-level `SaveChangesAsync` → `ConcurrencyConflictException`, already used by `WorkAreaChangeRequest`/`LocationChangeRequest`/`DeviceChangeRequest`) onto `Sprint.Status`. WM-04 extends the existing `WorkApprovalEligibility` cross-project aggregator rather than building a new screen. SEC-13 adds an additive magic-byte verification step to the existing shared `UploadPurposePolicy`, and a `NotScanned`-only scanner abstraction — no new malware-scanning product dependency.

**Tech Stack:** .NET / EF Core / Postgres (backend, `HRMS-Backend-v1`), Angular (frontend, `Hrms--Web-application---front-end---v1`), xUnit + Moq + FluentAssertions (unit tests), Testcontainers-Postgres (integration tests, not used by this plan — see Task 1 test-scope note).

**Spec:** None pre-existing; this plan's own Phase-0 evidence (below) is the spec, gathered by direct repo reading and five parallel Explore sub-agent investigations on 2026-10-12.

## Global Constraints

- Both repos are on branch `feature/wm-milestones-page-redesign`. Backend tree is clean; frontend tree has a teammate's uncommitted, unrelated changes in `dashboard/top-navbar` and `dashboard/action-center-card`/`action-inbox.util.ts` — never touch those files, never stash/reset/discard them.
- Scope is Work Management only, except SEC-13 where the audit explicitly asks for a shared/centralized upload-validation fix — any shared-file change is called out explicitly in that task and kept additive (never narrows existing allowlists, never changes other modules' accepted file types).
- No DB column/data removal. No change to WM-03 (sprint is project-scoped) or WM-05 (overdue = notify only) behavior.
- No fake/placebo malware scanning. A disabled/unconfigured scanner must report `NotScanned`, never `Clean`.
- No production migration applied by this agent — migrations are written and left for the user to run.
- Follow the existing per-aggregate `IXxxRepository.SaveChangesAsync()` → catch `DbUpdateConcurrencyException` → throw `ConcurrencyConflictException` convention (see `EfWorkAreaChangeRequestRepository.cs:126-141`) rather than touching the shared `IUnitOfWork`.

## Review Focus

- A sprint Start/Complete/Achieve/Edit/Delete double-click or client retry must get exactly one applied side effect and one clean 409 on the loser — not a 500, not two applied side effects. (Task 1)
- Adding a concurrency token to `Sprint.Status` must not turn ordinary single-request Create/Edit/Delete/Achieve into unexpected 500s once two concurrent requests legitimately race — every submitter call site needs the translated-exception path, not just Start/Complete. (Task 1)
- SEC-13's signature check must not reject legitimate existing files already accepted today (e.g. a real `.docx`/`.xlsx`, which are ZIP-signature at the byte level, same as `.zip`) — ambiguous formats must be handled explicitly, not rejected outright. (Task 5)
- WM-04's "HR-sourced approval with no owned module" gap: the capability gate already says "you have something pending" while the content list returns empty for the same user — any fix must close that inconsistency without over-broadly exposing unrelated projects. (Task 4)
- WM-01 annotation must not silently change serialization/DTO shape (no field removal) — doc-comments only, zero behavior change, so existing task-status round-trip tests keep passing untouched. (Task 2)

---

## Per-Finding Status (Phase 0 evidence)

| ID | Status | Evidence |
|---|---|---|
| WM-01 | **NEEDS DECISION** (dead fields, no written decision found) | `TaskStatus.RequiresApproval`/`ApproverId` (`src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs:29-30`) are 100% CRUD-wired (backend DTOs/handlers, frontend model/form) but never read by any gating logic anywhere in git history (confirmed via `git log -S"RequiresApproval" -- .../MoveTaskStatus`: zero hits). No written "2026-09-28" decision document naming these two fields was found in `docs/`, `second-brain/`, or either repo's commit history — the audit's date/quote could not be independently verified. Real status-move governance (`MoveTaskStatusCommandHandler`) and the real approval engine (`WorkApprovalRequest`/`IWorkApprovalEngine`, built 2026-09-28/29) are both separate mechanisms that never consult these columns. |
| WM-02 | **REAL BUG — fixed in this plan** | `Sprint` has no concurrency token (`src/ONEVO.Domain/.../Sprint.cs:19-37`, `SprintConfiguration.cs:7-19`). `StartSprintCommandHandler`/`CompleteSprintCommandHandler` (and `Achieve`/`Edit`/`Delete`) load a tracked sprint, check `Status` in memory, mutate in memory, then `SaveChangesAsync` once at the end of `SprintActionSubmitter.SubmitAsync` (`SprintActionSubmitter.cs:88,102`) — two concurrent requests both pass the check and both persist (last-write-wins), double-moving tasks and double-notifying. No rate limiter covers Sprint routes. No existing concurrency test. The repo already has a proven fix idiom for exactly this (`WorkAreaChangeRequestConfiguration.cs:18`, `EfWorkAreaChangeRequestRepository.cs:126-141`). |
| WM-03 | **CONFIRMED BY DESIGN — verify/document only** | `Sprint.ProjectId` (`Sprint.cs:21`) is the only scoping FK; no Module FK exists; `20260923100313_DropSprintObjiveId` deliberately dropped the old module-scoping column. Design doc: `docs/superpowers/specs/finished/2026-09-23/2026-09-23-wm-independent-sprints-design.md:10,18`. The UI's "Add sprint" action on a Module row and the Tree tab's task-membership-derived nesting under a Module create a *visual* impression of module-scoping, but the create payload never sends a module id (`sprint-form.component.ts:177-193`, `sprint-api.service.ts:26-28`). No code change. |
| WM-04 | **PARTIALLY IMPLEMENTED — narrow gap closed in this plan, rest documented** | Two surfaces exist: a per-project `WorkApprovalsController` feed (`GetProjectApprovalFeedQueryHandler`) and a genuine cross-project aggregator (`GetTeamActionItemsQueryHandler` → dashboard Action Center card) that already does server-side-identity, permission-correct, cross-project aggregation for 9 action types. Gaps: (a) excludes `ModuleMemberAdd/Remove`, `TaskDelete`, `TaskStatusChange`, all `Sprint.*` types, and module invitations — **by explicit documented design** (`MyTeamApprovalActionTypes.cs:12-15`), a scope decision, not a bug; (b) **a real, narrow inconsistency**: the capability gate `HasPendingWorkApprovalsAsync` correctly flags an HR-sourced approver even if they own no module anywhere, but `ListDecidableAcrossLedProjectsAsync`'s *content* discovery only looks at owned-module projects, so that same approver's HR-sourced pending item never appears in the list content — fixed in this plan (Task 4). Expanding the 9-action-type scope or adding module invitations is a product scoping decision, left to the user. |
| WM-05 | **CONFIRMED BY DESIGN — verify/document only + one regression test** | `SprintStatuses` has only `Draft/Active/Complete/Achieved` — no `Incomplete`/`Overdue` status exists. Every one of the 6 `Sprint.Status` write sites (`SprintWriteService.cs:160,216,248,324,339` + `SprintStartReverter.cs:34`) traces to an explicit authenticated command or an explicit approve/revert decision — none are timer/background-driven. `ProjectMonitorService`/`ProjectMonitorRules` (the overdue detector) has no `ISprintRepository`/write dependency at all — structurally incapable of mutating a sprint; it only opens/resolves `MonitorAlert` rows and sends one notification. Plan doc `docs/superpowers/plans/next/2026-10-05-wm-notification-completion.md:35-37` confirms the old auto-transition job was deliberately removed. One regression test added (Task 3) pinning "overdue detection never touches Sprint.Status".
| SEC-13 | **PARTIAL — hardening shipped, real scanning BLOCKED** | Every WM upload path (`TasksController` pending-uploads, `ProjectsController` logo/banner) already routes through the shared `IFileStorageService`/`UploadPurposePolicy` — good, no WM-side bypass. That shared validator trusts caller-supplied Content-Type + filename extension only (no byte inspection) except for `EmployeeAvatar` (which decodes real pixels via SkiaSharp). `FileRecord.Status` (`PendingScan/Available/Quarantined/Deleted`) and `ScanProvider`/`ScanResultCode` columns already exist but are **never populated or gated on** — every file is created `PendingScan` and served identically regardless of status; no malware scanner of any kind exists in the repo. Fix in this plan: additive magic-byte signature check + populate `DetectedContentType` + gate the WM read path on `Status != Quarantined` (a no-op today, future-proofing) + a `IMalwareScanner`/`NullMalwareScanner` abstraction that only ever reports `NotScanned`. Flipping every upload to require `Available`-before-serving is **not** done — it would break 100% of existing downloads platform-wide since nothing scans today; that is the SEC-13 decision gate (Task 5). |

---

## Task 1: WM-02 — Atomic Sprint Start/Complete (and Achieve/Edit/Delete)

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/SprintConfiguration.cs:14`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintActionSubmitter.cs:16-105`
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddSprintStatusConcurrencyToken.cs` (+ `.Designer.cs`, snapshot update) via `dotnet ef migrations add`
- Modify (test wiring): `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintTestWiring.cs`, `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintEngineKit.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintConfigurationTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EfSprintRepositoryConcurrencyTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintConcurrencyTests.cs`

**Interfaces:**
- Produces: `ISprintRepository.SaveChangesAsync(CancellationToken ct = default) : Task<int>` — throws `ConcurrencyConflictException` (from `ONEVO.Application.Common.Exceptions`) on a stale write, `UniqueConstraintConflictException` on a unique-constraint race (mirrors `EfWorkAreaChangeRequestRepository.SaveChangesAsync`).
- Consumes: existing `ConcurrencyConflictException` (`src/ONEVO.Application/Common/Exceptions/ConcurrencyConflictException.cs`).

**Design decision (why this shape, not a handler-level fix):** `SprintActionSubmitter.SubmitAsync` is the single chokepoint every sprint write command (Create/Edit/Delete/Start/Complete/Achieve) already funnels through. Fixing concurrency once here, at the entity+submitter level, protects all six actions uniformly instead of patching Start/Complete only and leaving Edit/Delete/Achieve newly exposed to an *unhandled* `DbUpdateConcurrencyException` (500) once the token exists. The `_unitOfWork.ExecuteInTransactionAsync(...)` call stays as the transaction boundary (BeginTransaction/Commit); only `SaveChangesAsync` moves from the shared `IUnitOfWork` to `ISprintRepository`, matching this repo's established per-aggregate catch convention, and the catch for `ConcurrencyConflictException` wraps the *outer* `ExecuteInTransactionAsync` call (not the inner lambda) so that on conflict the exception propagates through the `await using var transaction` block, which rolls back instead of committing a half-applied batch.

- [ ] **Step 1: Add the concurrency token to the entity configuration**

```csharp
// SprintConfiguration.cs:14
builder.Property(s => s.Status).HasMaxLength(20).IsRequired().IsConcurrencyToken();
```

- [ ] **Step 2: Add `SaveChangesAsync` to `ISprintRepository` and implement it in `EfSprintRepository`**

`ISprintRepository.cs` — add to the interface:
```csharp
Task<int> SaveChangesAsync(CancellationToken ct = default);
```

`EfSprintRepository.cs` — add (needs `using ONEVO.Application.Common.Exceptions;`):
```csharp
public async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    try
    {
        return await _db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException ex)
    {
        throw new ConcurrencyConflictException(ex);
    }
}
```

- [ ] **Step 3: Switch `SprintActionSubmitter` to save through the repository and translate the conflict**

Constructor: add `ISprintRepository sprints` parameter, store as `_sprints`, add `using ONEVO.Application.Common.Exceptions;` and `using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;`.

Replace both `await _unitOfWork.SaveChangesAsync(innerCt);` call sites (lines 88 and 102) with `await _sprints.SaveChangesAsync(innerCt);`.

Wrap the `return await _unitOfWork.ExecuteInTransactionAsync(...)` block (lines 72-104) in:
```csharp
try
{
    return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
    {
        /* unchanged body, with the two SaveChangesAsync calls switched per above */
    }, ct);
}
catch (ConcurrencyConflictException)
{
    return Result<SprintActionOutcome>.Conflict(
        "This sprint was just updated by another request. Please refresh and try again.");
}
```

- [ ] **Step 4: Add the migration**

```bash
dotnet ef migrations add AddSprintStatusConcurrencyToken --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.Api/ONEVO.Api.csproj --context ApplicationDbContext
```
Verify the generated `Up()` only alters the `status` column's concurrency metadata on `sprints` (no other table touched); EF may generate an empty or no-op SQL body since `IsConcurrencyToken()` on an existing non-nullable string column often needs no DDL — if `Up()`/`Down()` come back empty, that's expected (same as the attendance-corrections precedent in this repo) and the migration still exists so `dotnet ef migrations has-pending-model-changes` stays clean. **Do not apply this migration** — leave it for the user to run against a real database.

- [ ] **Step 5: Update test wiring for the new constructor parameter**

`SprintTestWiring.cs` constructor: add
```csharp
Sprints.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
```
(mirrors the existing `UnitOfWork.Setup(x => x.SaveChangesAsync(...)).ReturnsAsync(1);` at line 46) and update `Submitter()`:
```csharp
public SprintActionSubmitter Submitter() => new(Hierarchy.Object, Members.Object, Identity.Object, UnitOfWork.Object,
    Approvals.Object, WorkNotifications.Object, Sprints.Object);
```
`SprintEngineKit.cs` — update its private `Submitter()` (line 109-113) the same way, passing `W.Sprints.Object`.

- [ ] **Step 6: Run the existing sprint unit tests to confirm the wiring change didn't break anything**

Run: `dotnet build src/ONEVO.Application && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement.Sprints"`
Expected: all pre-existing sprint tests still PASS (constructor signature change only, no behavior change for the success path).

- [ ] **Step 7: Model-metadata test — Status really is a concurrency token**

Add to `SprintConfigurationTests.cs` (needs `using ONEVO.Infrastructure.Persistence;` and a way to build a `DbContext` the way `EfSprintRepositoryTests.BuildInMemoryDb()` does — either reuse that helper via a shared fixture or inline a minimal one):
```csharp
[Fact]
public void Sprint_StatusIsConfiguredAsConcurrencyToken()
{
    using var db = BuildInMemoryDb();
    var status = db.Model.FindEntityType(typeof(Sprint))!.FindProperty(nameof(Sprint.Status));

    status.Should().NotBeNull();
    status!.IsConcurrencyToken.Should().BeTrue();
}
```
Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SprintConfigurationTests"`
Expected: PASS.

- [ ] **Step 8: Repository-level race test proving the conditional update actually rejects a stale write**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/EfSprintRepositoryConcurrencyTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfSprintRepositoryConcurrencyTests
{
    [Fact]
    public async Task SaveChangesAsync_SecondWriterWithStaleStatus_ThrowsConcurrencyConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sprintId = Guid.NewGuid();

        await using (var seedDb = BuildDb(dbName))
        {
            seedDb.Sprints.Add(new Sprint
            {
                Id = sprintId, TenantId = tenantId, ProjectId = projectId, Name = "S1",
                Status = SprintStatuses.Active, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await seedDb.SaveChangesAsync();
        }

        // Two independent contexts = two concurrent requests, each with their own tracked copy.
        await using var dbA = BuildDb(dbName);
        await using var dbB = BuildDb(dbName);
        var repoA = new EfSprintRepository(dbA);
        var repoB = new EfSprintRepository(dbB);

        var sprintA = await repoA.GetTrackedByIdForTenantAsync(tenantId, sprintId, CancellationToken.None);
        var sprintB = await repoB.GetTrackedByIdForTenantAsync(tenantId, sprintId, CancellationToken.None);

        sprintA!.Status = SprintStatuses.Complete;
        sprintB!.Status = SprintStatuses.Complete;

        await repoA.SaveChangesAsync(); // winner: persists first, succeeds.

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repoB.SaveChangesAsync()); // loser: stale Status.
    }

    private static ApplicationDbContext BuildDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object));
    }
}
```
(If `ApplicationDbContext`'s constructor signature differs from this guess — e.g. it also takes a tenant-filter interceptor — match whatever `EfSprintRepositoryTests.BuildInMemoryDb()` actually passes; copy that helper's exact parameter list instead of guessing.)

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfSprintRepositoryConcurrencyTests"`
Expected: PASS — proves the InMemory provider (which does enforce `IsConcurrencyToken` per-entity original-value checks since EF Core 3.0) throws exactly the exception type the repository now translates.

- [ ] **Step 9: Application-layer double-click test — two concurrent `CompleteSprintCommandHandler.Handle` calls, one wins, one gets 409**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintConcurrencyTests.cs`. Build two `SprintEngineKit`-style wirings that share the SAME InMemory database name for `ISprintRepository` (real `EfSprintRepository` + real `ApplicationDbContext`, not mocked) while keeping `Hierarchy`/`Approvals`/`WorkNotifications`/`Members`/`Identity`/`CurrentUser`/`Tasks`/`Assignment`/`Logs` mocked exactly as `SprintTestWiring` already does (Approvals → Direct). Seed one `Active` sprint visible to both wirings (same `sprintId`/`tenantId`), build two `CompleteSprintCommandHandler`s (one per wiring), and run:
```csharp
var completeA = handlerA.Handle(new CompleteSprintCommand(sprintId, SprintCompleteDisposition.MoveToBacklog, null), CancellationToken.None);
var completeB = handlerB.Handle(new CompleteSprintCommand(sprintId, SprintCompleteDisposition.MoveToBacklog, null), CancellationToken.None);
var results = await Task.WhenAll(completeA, completeB);

results.Count(r => r.IsSuccess).Should().Be(1);
results.Count(r => !r.IsSuccess && r.StatusCode == 409).Should().Be(1);
```
(Check the real `CompleteSprintCommand`/`SprintCompleteDisposition` shape in `CompleteSprintCommandHandlerTests.cs` before writing this — use whatever constructor args that file already uses, don't guess the enum name.)

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SprintConcurrencyTests"`
Expected: PASS.

- [ ] **Step 10: Full backend unit suite, then commit**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit`
Expected: all green, no regressions outside the files touched above.
Leave uncommitted for the user's final review (per Hard Rule: do not auto-commit/push).

**Explicitly out of scope for this task:** a real-Postgres/WebApplicationFactory HTTP-level concurrent-request integration test. Building one from scratch requires the full tenant-provisioning/login fixture (`CreateProjectEndpointTestsFixture`-style, ~150 lines of setup) for a scenario the InMemory-provider application-layer test in Step 9 already exercises faithfully (real `EfSprintRepository`, real optimistic-concurrency check, real `SprintActionSubmitter`, genuinely parallel `Task.WhenAll`). Flagged here rather than silently skipped — if the user wants true Postgres-backed coverage, it is a follow-up, not a blocker to calling WM-02 fixed.

---

## Task 2: WM-01 — Document as NEEDS DECISION, zero-risk annotation only

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs:29-30`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (the three DTOs at lines ~33, ~38, ~76-77)
- Modify (frontend): `src/app/modules/work/models/dto/task.dto.ts:7-8`

**No behavior change. No column/DTO-field removal (decision-gated per Hard Rule 3 — see below).**

- [ ] **Step 1: Add an explicit XML-doc / comment on the backend fields stating they are dead**

```csharp
/// <summary>
/// Persisted and round-tripped through the API for backward compatibility, but NOT read by any
/// approval/gating logic anywhere in this codebase (confirmed via full-history grep as of 2026-10-12).
/// Real task-status-move governance lives in MoveTaskStatusCommandHandler; the real approval engine is
/// WorkApprovalRequest/IWorkApprovalEngine. Do not add new logic that branches on this field without
/// first removing this comment and confirming the decision with product.
/// </summary>
public bool RequiresApproval { get; set; }

/// <summary>See <see cref="RequiresApproval"/> — same dead-field status.</summary>
public Guid? ApproverId { get; set; }
```

- [ ] **Step 2: Mirror a short version of the same note on the three DTOs and the frontend model field**, e.g. `// Dead field — see TaskStatus.RequiresApproval doc comment. Not used by any gating logic.`

- [ ] **Step 3: Build and run the existing TaskStatus tests to confirm zero behavior change**

Run: `dotnet build src/ONEVO.Application && dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskStatus"`
Expected: PASS, unchanged (comment-only diff).

**Decision gate (per Hard Rule 3 / WM-01's own instructions): do NOT go further than this without explicit product sign-off.** Two options for the user:
- **Option A (recommended default):** Leave as annotated dead fields indefinitely. Zero further risk, zero migration, fields remain available if a future feature wants to repurpose them (though a new feature should probably not reuse a confusingly-named dormant field).
- **Option B:** Full removal — drop the column via a migration, remove the two DTO fields (breaking API contract change for any external consumer), remove the frontend form inputs. Requires explicit product approval because it is a destructive schema change and a contract-breaking change; not attempted in this plan.

---

## Task 3: WM-03 & WM-05 — Verification documentation + one WM-05 regression test

**Files:**
- Create: `docs/superpowers/plans/next/2026-10-12-wm-03-wm-05-verification-evidence.md` (the verification write-up, evidence already gathered — see Per-Finding Status table above; this file is just that evidence formatted for hand-off, no new investigation needed)
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Monitoring/ProjectMonitorServiceNeverWritesSprintTests.cs` (exact path: match wherever `ProjectMonitorService`'s existing tests already live — check `tests/ONEVO.Tests.Unit/Features/WorkManagement/**/*ProjectMonitor*` first and add to that folder instead of inventing a new one)

- [ ] **Step 1: Write the verification doc** — copy the WM-03 and WM-05 rows from the Per-Finding Status table above into their own file, each with the full file:line citation list from Phase 0 (already gathered by the Explore sub-agents this session; no new reading needed).

- [ ] **Step 2: Add the WM-05 regression test** — assert, by reading `ProjectMonitorService`'s constructor dependencies via reflection or simply by compiling a test that constructs it with ONLY read-side collaborators (no `ISprintRepository`/`ISprintWriteService`/`IWorkTaskRepository` in its constructor parameter list), that it is structurally incapable of writing a sprint. If a cleaner behavioral test is possible (e.g. run `EvaluateProjectAsync` against a snapshot with an overdue active sprint and assert no `ISprintRepository.Update`/`SaveChangesAsync`-style call occurred), prefer that — check the real constructor signature and existing test fixtures for `ProjectMonitorService` first (`ProjectMonitorService.cs:22-27`) before writing this test, since the exact collaborator list must be read from the file, not guessed.

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ProjectMonitor"`
Expected: PASS.

**No other code changes for WM-03 or WM-05 — both are confirmed-by-design per Phase 0 evidence.**

---

## Task 4: WM-04 — Close the HR-sourced-approval content gap (narrow fix), document the rest

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Leadership/Services/WorkApprovalEligibility.cs` (the `ListDecidableAcrossLedProjectsAsync` method, ~lines 47-63)
- Check/modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkApprovalRequestRepository.cs` (needs a way to list projects with an HR-sourced pending approval for a given approver — check whether `HasPendingForHrApproverAsync`, ~line 62-67, can be reused/extended to return project ids rather than just a bool, or whether a sibling method is needed)
- Modify tests: `tests/ONEVO.Tests.Integration/MyTeam/WorkApprovalEligibilityIntegrationTests.cs` (extend `HrSourcedApproval_IsUnaffectedByAnyHierarchyOwnershipChange`, or add a sibling test asserting the row now appears in *content*, not just the gate)
- Modify tests: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Leadership/WorkApprovalEligibilityTests.cs`

**Design decision:** `ListDecidableAcrossLedProjectsAsync` currently discovers candidate projects via `objectives.ListActiveOwnedIdsAsync(...)` only (owned-module projects). It must ALSO union in project ids where the caller has an HR-sourced pending approval (the same source the capability gate `HasPendingForHrApproverAsync` already queries), so that a caller who owns zero modules anywhere but is the HR-designated approver for a pending request still sees it in their aggregated content — closing the inconsistency between "the gate says you have something pending" and "the list is empty."

- [ ] **Step 1: Read the exact current implementation before changing it** — `WorkApprovalEligibility.cs:47-63`, `EfWorkApprovalRequestRepository.cs` (`ListProjectsWithPendingAsync` ~line 69-80 and `HasPendingForHrApproverAsync` ~line 62-67), and the existing passing test `HrSourcedApproval_IsUnaffectedByAnyHierarchyOwnershipChange` (`WorkApprovalEligibilityIntegrationTests.cs:117-157`) and `GateAndContent_NeverDisagree_ForTheSameData` (`:160`) to confirm the exact current contract before writing the union logic — do not guess method signatures from this plan alone.

- [ ] **Step 2: Add a repository method returning project ids with an HR-sourced pending approval for this approver** (name and shape to match the existing `ListProjectsWithPendingAsync` convention in the same file), then union those project ids into the candidate set in `ListDecidableAcrossLedProjectsAsync` before the existing "narrow to projects with an actual pending row" step.

- [ ] **Step 3: Write the failing test first** (TDD) — extend or add to `WorkApprovalEligibilityIntegrationTests.cs`: seed an HR-sourced pending `WorkApprovalRequest` for an employee who owns zero modules in any project, call `ListDecidableAcrossLedProjectsAsync`, assert the request IS now in the returned content (not just that `HasPendingWorkApprovalsAsync` returns true).

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~WorkApprovalEligibility"` (requires Docker for Testcontainers-Postgres)
Expected: FAILS before Step 2's fix, PASSES after.

- [ ] **Step 4: Confirm `GateAndContent_NeverDisagree_ForTheSameData` still passes** (this is the test that would catch a regression where the gate and content disagree in the OTHER direction).

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~WorkApprovalEligibility"`
Expected: all PASS.

- [ ] **Step 5: Document the remaining, explicitly-scoped-out gaps as a product decision, not a bug** — write into the verification/plan doc: expanding `MyTeamApprovalActionTypes` beyond the current 9 types (to include module-membership, task-delete, task-status-change, all `Sprint.*` approvals) and adding module invitations to the aggregator are both deliberate, documented scope limits (`MyTeamApprovalActionTypes.cs:12-15`) — present as **Option A: leave as-is** (recommended default, matches audit's own note that WM-04 was accepted as a known UAT limitation) vs **Option B: expand scope** (requires product sign-off on which action types to add, plus new tests per type — out of scope for this plan).

**If Step 1's reading reveals the repository/query shape is materially more complex than this plan assumes** (e.g. `ListProjectsWithPendingAsync` already does something that makes a clean union non-trivial), stop at this task's decision gate, document the actual shape found, and leave the fix as a documented recommendation rather than forcing a risky change — do not guess.

---

## Task 5: SEC-13 — Additive upload hardening (shared file, explicitly scoped), real scanning BLOCKED

**Files:**
- Modify: `src/ONEVO.Infrastructure/Services/Storage/File/UploadPurposePolicy.cs:13-61` (add magic-byte signature check, additive only)
- Modify: `src/ONEVO.Infrastructure/Services/Storage/File/FileStorageService.cs:194` (populate `DetectedContentType` instead of leaving it `null`)
- Create: `src/ONEVO.Application/Features/Storage/File/ServiceInterfaces/IMalwareScanner.cs` (new interface: `Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct)` returning an enum `{ NotScanned, Clean, Infected, Failed }` — never a hardcoded `Clean`)
- Create: `src/ONEVO.Infrastructure/Services/Storage/File/NullMalwareScanner.cs` (always returns `NotScanned`, with an XML-doc explaining why, and DI registration in whatever `DependencyInjection.cs` already wires `IFileStorageService`)
- Modify (WM read-gate, future-proofing, no-op today): `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskFile/GetTaskFileQueryHandler.cs` — reject (404/403, matching existing not-found semantics) if `FileRecord.Status == FileRecordStatus.Quarantined`
- Create: `tests/ONEVO.Tests.Unit/Features/Storage/UploadPurposePolicySignatureTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Storage/NullMalwareScannerTests.cs`

**Blast-radius note (explicit, not silent):** `UploadPurposePolicy.cs`/`FileStorageService.cs` are shared infrastructure used by Work Management, Leave, Monitoring, and CoreHr/OrgStructure avatar/logo linking. The SEC-13 finding itself asks for "a centralized upload pipeline or shared security service rather than scattered endpoint-only checks" — this task necessarily touches that shared choke point. Every change here is strictly additive (adds a NEW rejection case for genuine signature/extension mismatches; never narrows an existing allowed combination), so no other module's currently-working upload flow changes behavior. The one exception — `GetTaskFileQueryHandler`'s new `Status == Quarantined` check — is WM-scoped and a no-op today (nothing ever sets `Quarantined`).

- [ ] **Step 1: Write the failing signature-mismatch test first**

```csharp
// tests/ONEVO.Tests.Unit/Features/Storage/UploadPurposePolicySignatureTests.cs
[Fact]
public void ValidateUpload_PngBytesWithPdfExtension_IsRejected()
{
    byte[] pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    var result = UploadPurposePolicy.ValidateUpload(/* purpose, "file.pdf", "application/pdf", pngSignature-backed stream, size */);
    result.IsSuccess.Should().BeFalse();
}

[Fact]
public void ValidateUpload_ZipSignatureWithDocxExtensionAndContentType_IsAccepted()
{
    // .docx/.xlsx are ZIP-signature at the byte level — this must NOT be rejected as a mismatch.
}
```
(Match `UploadPurposePolicy.ValidateUpload`'s real current parameter list — read it first, don't guess the signature.)

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UploadPurposePolicySignatureTests"`
Expected: FAIL (method doesn't exist yet / check not implemented).

- [ ] **Step 2: Implement a small internal magic-byte table and the check**

Add a private static signature table covering: PNG (`89 50 4E 47`), JPEG (`FF D8 FF`), GIF (`47 49 46 38`), WEBP (`52 49 46 46` ... `57 45 42 50` at offset 8), PDF (`25 50 44 46`), ZIP/OOXML/`.docx`/`.xlsx` (`50 4B 03 04`), legacy OLE2 `.doc`/`.xls` (`D0 CF 11 E0`). For any extension in this table, read the file's first 8 bytes and confirm a match; if the extension isn't in the table (ambiguous/unknown), skip the byte check and fall back to today's extension+content-type check only (explicit "handle ambiguous formats" per the audit, not a silent reject-everything-unknown policy). On a signature table hit that does NOT match, reject with a clear error distinct from the existing extension-mismatch error.

- [ ] **Step 3: Run the Step-1 tests again, confirm they pass; run the full existing Storage test suite to confirm no existing accepted-file-type test starts failing**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Storage"`
Expected: all PASS, including every pre-existing `UploadPurposePolicy`/`FileStorageService` test (proves the change is additive).

- [ ] **Step 4: Populate `DetectedContentType`** from the same signature table lookup (map matched signature → canonical content type; leave `null` for unmatched/ambiguous as before — don't invent a value you don't have evidence for).

- [ ] **Step 5: Add the `IMalwareScanner`/`NullMalwareScanner` abstraction**

```csharp
public enum ScanOutcome { NotScanned, Clean, Infected, Failed }

public interface IMalwareScanner
{
    Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct = default);
}

/// <summary>No scanner is deployed. Always reports NotScanned — NEVER Clean — so callers that
/// gate on scan status fail closed/pending rather than silently trusting unscanned content.
/// Real scanning is BLOCKED pending a product decision on which scanner to deploy (see SEC-13
/// in docs/superpowers/plans/next/2026-10-12-pre-uat-audit-wm-sec-findings.md).</summary>
public sealed class NullMalwareScanner : IMalwareScanner
{
    public Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct = default)
        => Task.FromResult(ScanOutcome.NotScanned);
}
```
Register `IMalwareScanner → NullMalwareScanner` in the same DI module that registers `IFileStorageService`. Do NOT wire it into the upload flow to change `FileRecord.Status` yet — that would require deciding fail-open vs fail-closed platform-wide, which is this task's decision gate (see below). The interface exists so a real scanner can be dropped in later without further plumbing changes.

- [ ] **Step 6: Test the null scanner**

```csharp
[Fact]
public async Task ScanAsync_AlwaysReturnsNotScanned_NeverClean()
{
    var scanner = new NullMalwareScanner();
    var result = await scanner.ScanAsync(Stream.Null, CancellationToken.None);
    result.Should().Be(ScanOutcome.NotScanned);
}
```
Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~NullMalwareScannerTests"`
Expected: PASS.

- [ ] **Step 7: Add the WM-scoped, no-op-today quarantine read-gate**

In `GetTaskFileQueryHandler.cs` (and wherever else WM reads a `FileRecord` for download/preview — check `GetFileQueryHandler.cs` too since it's the shared one `FilesController` uses; if changing the shared one, confirm it's still additive since nothing sets `Quarantined` today so this can never reject an existing file), add: if `fileRecord.Status == FileRecordStatus.Quarantined`, return `NotFound`/`Forbidden` (matching whatever this handler already returns for a missing/deleted file) instead of streaming the content.

- [ ] **Step 8: Test the quarantine gate is wired but currently unreachable (proves no regression, documents intent)**

```csharp
[Fact]
public async Task Handle_QuarantinedFile_ReturnsNotFound()
{
    // Seed a FileRecord with Status = Quarantined directly (bypassing the normal upload path,
    // since nothing in production code sets this today) and assert the read handler blocks it.
}

[Fact]
public async Task Handle_PendingScanFile_StillServed_DocumentingCurrentBehavior()
{
    // Explicitly pins today's known-gap behavior (PendingScan served like Available) so a future
    // change to this is a deliberate, reviewed diff, not an accidental regression discovered later.
}
```
Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTaskFileQueryHandler"`
Expected: PASS.

- [ ] **Step 9: Full backend build + unit suite**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit`
Expected: all green.

**Decision gate (SEC-13 cannot be marked IMPLEMENTED — only PARTIAL):**
- **Option A (recommended default, matches "accepted as known UAT limitation, not a launch blocker"):** Ship this task's hardening now (signature check, `DetectedContentType`, scanner interface, future-proofed quarantine gate). Defer real scanning until a scanner is selected (e.g. a ClamAV sidecar container, or a cloud AV API) and infra/budget is approved. Leave every file served exactly as today (fail-open) until that's deployed — flipping to fail-closed today would block 100% of existing downloads platform-wide, since nothing has ever scanned anything.
- **Option B:** Immediately fail-closed every file whose `Status != Available` (i.e. everything, since nothing is ever set to `Available` today). This blocks all existing uploads/downloads across every module until a backfill + a real scanner both ship — a severe, platform-wide availability regression. Not recommended, and not attempted in this plan without explicit approval.

---

## Execution Checklist

- [x] Task 1 (WM-02): entity config, repository, submitter, migration, test-wiring updates, new tests, full suite green. Implemented exactly as planned (Steps 1-10). Migration `20261012074702_AddSprintStatusConcurrencyToken` generated, NOT applied.
- [x] Task 2 (WM-01): doc-comment annotations (backend + frontend). Zero behavior change, confirmed by full suite green.
- [x] Task 3 (WM-03/WM-05): verification doc written (`2026-10-12-wm-03-wm-05-verification-evidence.md`), WM-05 regression tests added (constructor-shape + behavioral).
- [x] Task 4 (WM-04): HR-sourced content gap closed in `WorkApprovalEligibility.ListDecidableAcrossLedProjectsAsync` + `IWorkApprovalRequestRepository.ListPendingForHrApproverAsync`; unit + integration tests added and green against real Postgres. Remaining action-type-scope gaps (module membership, task delete/status-change, sprint approvals, module invitations) documented as a product scoping decision, not implemented.
- [x] Task 5 (SEC-13): signature check (`UploadPurposePolicy.ValidateSignature`) wired into `FileStorageService.UploadAsync`, `DetectedContentType` populated, `IMalwareScanner`/`NullMalwareScanner` added (NotScanned-only, DI-registered but not wired into any gating decision), WM-and-shared quarantine read-gate added in `FileStorageService.OpenReadAsync` (no-op today), tests added. Real scanning explicitly BLOCKED - see Task 5's decision gate.
- [x] Final validation pass: `dotnet build` clean across Application/Infrastructure/Api; full `dotnet test tests/ONEVO.Tests.Unit` green (5593+ tests); `tests/ONEVO.Tests.Architecture` green except one pre-existing, unrelated failure (`DapiDueTodayTaskSeeder.cs` IgnoreQueryFilters allowlist - confirmed pre-dates this session, commit `164e1458`, 2026-10-09); relevant `tests/ONEVO.Tests.Integration` suites green (WorkApprovalEligibility x4, FileStorage x4, TaskAttachments x4, TaskComments x7, plus 62 out-of-WM-scope Monitoring/OrgStructure/Security tests confirmed unaffected by the SEC-13 shared-code change).
- [x] Final report written per the master prompt's mandated format, with file:line evidence for every claim.
