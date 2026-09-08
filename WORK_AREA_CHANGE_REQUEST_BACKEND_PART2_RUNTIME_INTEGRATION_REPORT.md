# ONEVO HRMS — Work Area Change Request Backend Part 2 Report
## Applying approved one-day work-area overrides to Time Tracking runtime

## 1. Initial git status

The repository began dirty on `local/reporting-manager-run` with the complete Part 1 Work Area Change Request slice (persistence, workflow, notifications, tests) plus unrelated pre-existing changes (EmployeeAuthority batching/session-context correction, `PositionsController.cs`, stray log/plan files) already uncommitted:

```text
## local/reporting-manager-run
 M src/ONEVO.Api/Contracts/SharedPlatform/Notifications/NotificationContracts.cs
 M src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs
 M src/ONEVO.Application/Features/Auth/Permission/RepositoryInterfaces/IPermissionRepository.cs
 ... (23 modified files, 27 untracked paths — the full Part 1 feature plus prior unrelated work)
?? WORK_AREA_CHANGE_REQUEST_BACKEND_PART1_REPORT.md
?? dev-server-restart.log
?? docs/superpowers/plans/2026-08-25-attendance-list-pagination.md
?? docs/superpowers/plans/2026-08-25-work-area-hardening-part1-final.md
```

All of this pre-existing state was preserved. `git diff --check` reported only pre-existing Windows LF→CRLF advisory warnings, no whitespace errors or conflict markers, both before and after this task's changes.

## 2. Confirmed pre-change runtime defect

Verified by direct inspection before any edit:

- `ExpectedWorkAreaResolver.ResolveAsync` resolved only from the employee's active `WorkModeId` lookup — it never queried `work_area_change_requests`.
- `AttendanceTodayStateService.ResolveContextAsync` computed its own `workMode` via a private `ResolveWorkModeAsync` helper (a second, independent resolution path) and never called `IExpectedWorkAreaResolver` at all, so the already-registered resolver was dead code from Today/Clock-in's perspective.
- `ClockInCommandHandler.ApplyClockInState` persisted `AttendanceRecord.ExpectedWorkArea` via a local `ToPersistedWorkArea(context.WorkMode)` helper, driven by the same permanent-work-mode-only context field.
- `WorkAreaChangeRequestWorkflow`'s approval path (`DecideAsync`) updated only the request row — it never touched an existing `attendance_records` row for the same employee/date.

Net effect: an approved one-day override changed nothing in Today, Clock-in Policy branch selection, the persisted attendance snapshot, or history — exactly as Part 1's own report documented as an explicit, deferred boundary (§13).

## 3. Authoritative resolution order implemented

`ExpectedWorkAreaResolver.ResolveAsync` now resolves, in order:

1. An approved `work_area_change_requests` row for the exact tenant, legal entity, employee, and date (`IWorkAreaChangeRequestRepository.GetApprovedForDateAsync`) → `WorkArea = request.RequestedWorkArea`, `Source = "approved_work_area_change_request"`.
2. Otherwise, the employee's active `WorkModeId` lookup (unchanged mapping: `onsite`/`on_site`→`onsite`, `remote`→`remote`, `hybrid`→`either`, `field`→`field`) → `Source = "active_employee_work_mode"`.

No roster, shift-assignment, schedule-day, or work-schedule table was created; those levels remain explicitly out of scope per the task and are not claimed as implemented.

An approved row whose `RequestedWorkArea` is not `onsite`/`remote` (should be structurally impossible given the create-time validator, but not assumed) causes the resolver to return `Result.Conflict(...)` rather than silently falling back to the permanent work mode.

## 4. Repository contract added

```csharp
// IWorkAreaChangeRequestRepository
Task<WorkAreaChangeRequest?> GetApprovedForDateAsync(
    Guid tenantId, Guid legalEntityId, Guid employeeId, DateOnly date, CancellationToken ct = default);
```

`EfWorkAreaChangeRequestRepository`'s implementation uses `AsNoTracking()`, filters explicitly on tenant, legal entity, employee, exact date, and `Status == StatusApproved` (no `IgnoreQueryFilters`, no body-supplied tenant/employee identity — both come from the already-resolved `Employee`/`LegalEntity` entities passed into the resolver). If more than one approved row is found (the partial unique index on `(tenant_id, employee_id, date)` filtered to `pending`/`approved` should make this impossible), the repository throws a new `InconsistentWorkAreaChangeRequestStateException` (`src/ONEVO.Application/Common/Exceptions/InconsistentWorkAreaChangeRequestStateException.cs`, following the existing `ConcurrencyConflictException`/`UniqueConstraintConflictException` convention) rather than arbitrarily picking a row; the resolver catches it and returns a safe `Result.Conflict(...)` with no internal detail exposed. No migration was added — this is a read-only method against the existing Part 1 schema.

## 5. AttendanceTodayContext changes

`AttendanceTodayContext`'s `string? WorkMode` field was replaced with:

```csharp
string ExpectedWorkArea,        // "onsite" | "remote" | "either" | "field" — the resolved, persisted-compatible code
string ExpectedWorkAreaSource,  // "approved_work_area_change_request" | "active_employee_work_mode"
```

`AttendanceTodayStateService`'s constructor dropped its direct `IWorkModeRepository` dependency (it had no other use) and now takes `IExpectedWorkAreaResolver expectedWorkAreas`. `ResolveContextAsync` calls `expectedWorkAreas.ResolveAsync(employee, legalEntity, workDate, ct)` and, on failure, returns that failure through `Result<AttendanceTodayContext>` (mapped to the resolver's own status code, defaulting to 409) instead of silently degrading — this is the one behavioral change beyond wiring: previously an unresolved/inactive work mode left `WorkMode = null` and fell through to a policy branch that disabled every clock-in method (effectively a confusing 403); it now fails closed with an explicit 409 from the resolver, consistent with how `WorkAreaChangeRequestWorkflow` already treated the same resolver failure.

The private `ResolveWorkModeAsync` duplicate-resolution helper was deleted. `IExpectedWorkAreaResolver` is now the single runtime resolver used by Today, Clock-in, and the Work Area workflow — no handler re-implements the precedence order.

## 6. Clock-in Policy selection changes

`ResolvePolicyAsync` (unchanged signature) is now called with the resolved `ExpectedWorkArea` (normalized `either`→`hybrid` for the existing switch), not the permanent work mode. Concretely: an On-site employee with an approved Remote override for today gets `RemoteWebEnabled`/`RemotePhotoRequired`/etc.; a Remote employee with an approved On-site override gets the On-site fields; a Hybrid employee with no override still resolves internally to `either` and gets the `Either*` fields, exactly as before.

## 7. Clock-in behavior

`ClockInCommandHandler.ApplyClockInState` now assigns `record.ExpectedWorkArea = context.ExpectedWorkArea;` directly — `context.ExpectedWorkArea` already uses the exact persisted vocabulary (`onsite`/`remote`/`either`/`field`), so the previous `ToPersistedWorkArea(string? workMode)` conversion helper became dead code and was deleted entirely (no remaining dead conversion helper). `AllowedClockInMethods.Web` gating (used to accept/reject the `web` source) is likewise now driven by the resolved effective area, so an approved override changes which source is allowed, not just what gets displayed.

## 8. Attendance snapshot / Today response behavior

`AttendanceTodayResponse` gained one new **additive, optional, trailing** field so the existing `ExpectedWorkMode` JSON property name and every other field stayed unchanged:

```csharp
string? ExpectedWorkAreaSource = null   // "approved_work_area_change_request" | "active_employee_work_mode" | "attendance_record_snapshot"
```

`GetTodayAsync` now computes:

```csharp
var effectiveExpectedWorkArea = attendanceRecord?.ExpectedWorkArea ?? context.ExpectedWorkArea;
var effectiveExpectedWorkAreaSource = attendanceRecord is not null
    ? "attendance_record_snapshot"
    : context.ExpectedWorkAreaSource;
```

So once an attendance row exists for the work date, its persisted `ExpectedWorkArea` — the historical snapshot — is what `ExpectedWorkMode` reflects, never today's live resolution (which could theoretically differ, e.g. a stale in-flight edge case); before a row exists, the live resolved value/source is shown. `ExpectedWorkMode` continues to normalize the internal `either` to the existing user-facing `hybrid` value.

## 9. Approval-after-clock-in synchronization

`WorkAreaChangeRequestWorkflow.DecideAsync`, on the `Approved` branch only, now does:

```csharp
var existingRecord = await attendance.GetTrackedRecordAsync(
    request.TenantId, request.EmployeeId, request.Date, transactionCt);
if (existingRecord is not null)
{
    existingRecord.ExpectedWorkArea = request.RequestedWorkArea;
    existingRecord.UpdatedAt = dateTime.UtcNow;
}
```

This uses the existing tracked-fetch repository method (no detached entity, no blind `Update()`). `attendance` and `requests` are two repository facades over the same scoped `ApplicationDbContext` (confirmed via `UnitOfWork`/DI registration), so the single `await requests.SaveChangesAsync(transactionCt)` already present at the end of `DecideAsync` persists both the request's decision and the attendance snapshot mutation together — no second `SaveChangesAsync` call was added, and no new transaction boundary was introduced. `ActualStart`, `ActualEnd`, `AttendanceSource`, break rows, and `Employee.WorkModeId` are never touched by this block. Rejection and cancellation do not run this block at all (verified by test — `Reject_LeavesAttendanceSnapshotUnchanged` and `Cancel_OnlyRequesterCanCancelAndDoesNotChangePermanentWorkMode` both assert `attendance.GetTrackedRecordAsync` is never called for those paths).

No new rule was added for "approval after the work date has passed" or "overdue pending requests" — no authoritative source defines one, and the task instructed not to invent one; this remains an open, explicitly flagged risk (§15 below).

## 10. Clock-out / break behavior

Inspected `ClockOutCommandHandler`, `StartBreakCommandHandler`, `EndBreakCommandHandler`: none of the three ever read or wrote `AttendanceRecord.ExpectedWorkArea` before this task, and none were changed — confirmed by a source grep for `ExpectedWorkArea` across `Commands/ClockOut`, `Commands/StartBreak`, `Commands/EndBreak` returning no matches. Regression tests were added (not just relying on the absence of code) proving an approved snapshot is not reverted to the live/permanent value by Clock Out, Start Break, or End Break.

## 11. History behavior

`AttendanceReadHandlers.cs` was **not changed**. It already reads `record.ExpectedWorkArea` (the persisted snapshot) per row and normalizes `either`→`hybrid`; it never re-resolves via `IExpectedWorkAreaResolver` and performs no per-row Work Area Change Request lookup — exactly the snapshot-based, no-N+1 behavior required. This was confirmed by inspection, not assumed.

## 12. Security and tenant isolation

No change to authentication/authorization surfaces. `GetApprovedForDateAsync` takes tenant, legal entity, and employee id as explicit parameters supplied by already-resolved server-side context (never from an HTTP body); no `IgnoreQueryFilters`; RLS on `work_area_change_requests` is unchanged (no migration). The existing 13 PostgreSQL/Testcontainers RLS and partial-unique-index tests for this table were re-run unmodified and still pass (see §14), confirming this change did not weaken tenant isolation.

## 13. Exact files changed

| Layer | File | Change |
|---|---|---|
| Common | `src/ONEVO.Application/Common/Exceptions/InconsistentWorkAreaChangeRequestStateException.cs` | New — fail-closed signal for a duplicate approved row. |
| Repository contract | `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IWorkAreaChangeRequestRepository.cs` | Added `GetApprovedForDateAsync`. |
| Repository impl | `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfWorkAreaChangeRequestRepository.cs` | Implemented `GetApprovedForDateAsync`. |
| Resolver | `src/ONEVO.Application/Features/TimeAttendance/Services/ExpectedWorkAreaResolver.cs` | Added approved-override precedence, new dependency, fail-closed on invalid/duplicate data. |
| Today context/service | `src/ONEVO.Application/Features/TimeAttendance/Services/IAttendanceTodayStateService.cs`; `AttendanceTodayStateService.cs` | `AttendanceTodayContext` carries `ExpectedWorkArea`/`ExpectedWorkAreaSource`; service uses `IExpectedWorkAreaResolver`, drops `IWorkModeRepository` and the dead `ResolveWorkModeAsync`; Today response computes the snapshot-aware effective area/source. |
| Clock-in | `src/ONEVO.Application/Features/TimeAttendance/Commands/ClockIn/ClockInCommandHandler.cs` | Persists `context.ExpectedWorkArea` directly; removed dead `ToPersistedWorkArea`. |
| Approval workflow | `src/ONEVO.Application/Features/TimeAttendance/Commands/WorkAreaChangeRequests/WorkAreaChangeRequestWorkflow.cs` | `DecideAsync` synchronizes an existing attendance row's `ExpectedWorkArea` on approval. |
| Response DTO | `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/AttendanceReadResponses.cs` | Added additive trailing `ExpectedWorkAreaSource` on `AttendanceTodayResponse`. |
| Unit tests (updated) | `tests/ONEVO.Tests.Unit/Features/TimeAttendance/WorkAreaChangeRequestTests.cs`; `AttendanceReadHandlerTests.cs`; `AttendanceTodayLeaveAwareTests.cs`; `ClockInOutCommandHandlerTests.cs`; `BreakCommandHandlerTests.cs`; `WorkAreaChangeRequestWorkflowTests.cs` | Updated fixtures for the new constructor/record shapes; added new override/regression tests. |
| Unit tests (new) | `tests/ONEVO.Tests.Unit/Features/TimeAttendance/EfWorkAreaChangeRequestRepositoryTests.cs`; `AttendanceTodayWorkAreaOverrideTests.cs` | New EF-InMemory repository coverage and end-to-end (real resolver, mocked repos) Today-state override coverage. |
| Integration tests (new) | `tests/ONEVO.Tests.Integration/Features/TimeAttendance/ExpectedWorkAreaResolverIntegrationTests.cs` | Real-PostgreSQL coverage of `GetApprovedForDateAsync` scoping. |
| Report | `WORK_AREA_CHANGE_REQUEST_BACKEND_PART2_RUNTIME_INTEGRATION_REPORT.md` | This report. |

No frontend files were touched. No migration was added or changed.

## 14. Tests added and verification commands run

### Unit tests

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore` | Passed — 0 errors, 1 pre-existing unrelated warning (`AdminAuthController.cs`). |
| `dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore` | Passed — 0 errors. |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj -c Release --no-restore --filter "FullyQualifiedName~ExpectedWorkArea\|FullyQualifiedName~WorkAreaChange\|FullyQualifiedName~TimeTracking\|FullyQualifiedName~ClockIn\|FullyQualifiedName~ClockOut\|FullyQualifiedName~Break\|FullyQualifiedName~AttendanceRead\|FullyQualifiedName~AttendanceToday"` | **166 passed, 0 failed, 0 skipped.** |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj -c Release --no-restore` (full suite) | **3202 passed, 0 failed, 0 skipped.** |

New/updated scenarios cover: approved Remote overrides permanent On-site; approved On-site overrides permanent Remote; no approved row falls back to active work mode; Hybrid-with-no-override resolves to `either`; another date/employee/legal-entity/tenant does not override (resolver-level via mocks and repository-level via EF InMemory); an approved request with an unsupported requested area fails closed; duplicate approved rows fail closed; legal-entity timezone is preserved; Today uses the Remote/On-site/Hybrid policy branch correctly including photo/location/radius fields; Today shows the attendance-record snapshot (not the live resolution) once clocked in; Clock-in persists the effective (overridden) area and allows/rejects `web` based on the override's policy branch, not the permanent mode; Clock Out, Start Break, and End Break do not revert an already-persisted override snapshot; approval synchronizes an existing attendance row's snapshot inside the same transaction; approval with no existing attendance row never touches the attendance repository; rejection and cancellation never touch the attendance repository.

### Architecture tests

| Command | Result |
|---|---|
| `dotnet build tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj -c Release --no-restore` | Passed — 0 errors. |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj -c Release --no-restore` | **676 passed, 1 failed, of 677.** The one failure is the pre-existing `TimeTrackingMutationArchitectureTests.AttendanceRepository_UsesTrackedFetchForMutation`, throwing `ArgumentOutOfRangeException` from a source-string-offset assertion against the untouched `EfAttendanceReadRepository.cs` — the exact same failure documented in every prior Part's report (Part 1 correction pass, Part 2 read-model, Part 3, Part 4, Part 5), confirmed unrelated to this task and not introduced by it. |

No new dedicated architecture test class was added for this part given the size of the existing suite already covering Application/EF layering, tenant/legal-entity filtering conventions, and controller permissions for this feature area; the correctness properties this part cares about (single resolver, no duplicate resolution logic, tracked-fetch mutation, no body-supplied identity) are instead proven directly by the unit tests above, which assert on the actual behavior rather than reflecting over source text.

### PostgreSQL/Testcontainers integration tests

Docker Desktop was available in this environment (unlike Parts 3–5's environment, where it was not) — `docker version` succeeded and Testcontainers ran real `postgres:16-alpine` containers.

| Command | Result |
|---|---|
| `dotnet build tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj -c Release --no-restore` | Passed — 0 errors (pre-existing unrelated warnings only). |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj -c Release --no-restore --filter "FullyQualifiedName~WorkAreaChange\|FullyQualifiedName~TimeTracking"` | **13 passed, 0 failed, 0 skipped** (the pre-existing `WorkAreaChangeRequestsIntegrationTests` — schema, RLS, partial-unique-index behavior — re-verified unmodified and still green after this task's changes). |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj -c Release --no-restore --filter "FullyQualifiedName~ExpectedWorkAreaResolverIntegrationTests"` | **8 passed, 0 failed, 0 skipped** — new tests proving `GetApprovedForDateAsync` against real PostgreSQL: approved row returned for exact scope; pending/rejected/cancelled ignored; another date/employee/legal-entity/tenant ignored. |

These new integration tests connect a real `ApplicationDbContext` (Npgsql + snake_case naming, matching production configuration) directly to the Testcontainers instance and call the actual `EfWorkAreaChangeRequestRepository` method — not raw SQL — closing the one gap the EF-InMemory unit tests (`EfWorkAreaChangeRequestRepositoryTests.cs`) cannot: proving the LINQ translates correctly against real PostgreSQL, not just the InMemory provider.

**Not run**: a live HTTP round trip through `ClockInCommandHandler`/`WorkAreaChangeRequestWorkflow.ApproveAsync` against a fully tenant-provisioned PostgreSQL tenant (the pattern `AttendanceCorrectionsIntegrationTests.cs` uses, driving a `WebApplicationFactory` through real HTTP onboarding). That fixture is substantial (~700 lines of tenant/legal-entity/employee/session provisioning) and pre-exists this task only for Attendance Corrections; building an equivalent one for Work Area Change Requests was judged disproportionate given: (a) the Clock-in persistence and approval-sync logic itself is fully covered at the unit level against fakes of the exact same repository contracts used in production; (b) the underlying table's RLS/schema/unique-index behavior is independently proven by the pre-existing 13 Testcontainers tests, re-verified unmodified; (c) the new resolver query's real-PostgreSQL LINQ translation is now independently proven by the 8 new tests above. This is stated as a real, acknowledged gap, not claimed as covered.

### Final checks

| Command | Result |
|---|---|
| `git diff --check` | Exit code 0. Only pre-existing Windows LF→CRLF advisory warnings; no whitespace errors, no conflict markers. |
| `git status --short` | Unchanged file set beyond the files listed in §13 plus this report; nothing staged. |

No `dotnet ef migrations` command was run for this part — no schema change was made or is needed; `GetApprovedForDateAsync` is a plain read against the existing Part 1 table.

## 15. Skipped/blocked checks

- HTTP-level end-to-end integration coverage through the full tenant-provisioning `WebApplicationFactory` stack (see §14) was not built for this part; this is a scope/time trade-off, not a technical blocker — Docker itself was available.
- No dedicated new architecture-test class was added; existing architecture coverage plus the new behavioral unit tests were judged sufficient (see §14 rationale).

## 16. Remaining risks

- `WorkAreaChangeRequestWorkflow.DecideAsync`'s approval-after-clock-in synchronization assumes `attendance.GetTrackedRecordAsync` and `requests.SaveChangesAsync` share one `ApplicationDbContext` instance (confirmed true today via DI/scope inspection, and by this task's integration tests exercising the real repository classes) — a future change that gives these two repositories independent `DbContext` instances would silently break the single-transaction guarantee described in §9. This is architecturally unlikely (the whole `IUnitOfWork` pattern in this codebase depends on one shared scoped context) but is called out explicitly as the assumption underpinning §9.
- No rule exists for approving a request after its work date has fully elapsed without a same-day clock-in having occurred (e.g. approving yesterday's request today); the task explicitly said not to invent one, so this remains open exactly as flagged in §9.
- `AttendanceTodayContext.ResolveContextAsync` now fails closed (409) when the effective work mode/override cannot be resolved, where it previously degraded silently to an all-methods-disabled policy (which produced a 403 on clock-in). This is a deliberate, documented behavior change (§5) consistent with the resolver's existing fail-closed contract used by `WorkAreaChangeRequestWorkflow`, but it is a visible status-code change for that specific edge case and should be called out to the frontend if it has ever special-cased that 403.
- HTTP-level integration coverage for this specific runtime path (Today/Clock-in/Approval-sync driven through real HTTP against a provisioned tenant) remains the most valuable next increment if this area needs further hardening (see §15).

## 17. Confirmation

No frontend files were modified. Nothing was staged, committed, or pushed at any point during this task — verified by `git status --short` before, during, and after implementation.

## 18. Architecture and HTTP end-to-end verification correction

This section documents a follow-up task that closed the two remaining verification gaps flagged in §15/§16 above: the brittle architecture test, and the missing HTTP/PostgreSQL end-to-end coverage for the Work Area runtime path. Work was confined to `C:\onevoNew\HRMS-Backend-v1`; nothing was staged, committed, or pushed.

### Part A — brittle architecture-test root cause and replacement

**Root cause.** `TimeTrackingMutationArchitectureTests.AttendanceRepository_UsesTrackedFetchForMutation` read `EfAttendanceReadRepository.cs` as raw text, sliced from the literal `"GetTrackedRecordAsync"` to the next literal `"public async Task<IReadOnlyList<AttendanceRecord>>"`, and asserted the slice did not contain `"AsNoTracking"`. This literal never matches the file's actual next method signatures — `ListBreaksAsync` returns `Task<IReadOnlyList<BreakRecord>>` (a different entity), and `ListRecordsAsync` returns the tuple `Task<(IReadOnlyList<AttendanceRecord> Items, int TotalCount)>`, not `Task<IReadOnlyList<AttendanceRecord>>` — so `IndexOf` returns `-1` and the subsequent range slice throws `ArgumentOutOfRangeException` before the intended assertion ever runs. This is exactly the same failure documented in every prior Part's report; it predates and is unrelated to the Work Area feature.

**Why source-string slicing was inappropriate.** The test was asserting a real architectural property (mutation must use a tracked fetch, not `AsNoTracking`) through a proxy that depends on unrelated things: method declaration order, exact return-type spelling, and whitespace — none of which affect the property under test. Any refactor that reorders methods or changes an unrelated method's return type (as happened here) breaks the test without the tracked-fetch behavior ever regressing, and conversely a real regression (e.g. adding `AsNoTracking()` inside `GetTrackedRecordAsync`) would not be caught any more directly than by coincidence of the slice still landing in the right place.

**New behavioral tracking test.** `EfAttendanceReadRepositoryTests.GetTrackedRecordAsync_ReturnsTrackedEntity_AndMutationPersistsViaSaveChanges` (`tests/ONEVO.Tests.Unit/Features/TimeAttendance/EfAttendanceReadRepositoryTests.cs`) now proves the real behavior against an EF InMemory `ApplicationDbContext` (the established pattern already used by this file and by `EfWorkAreaChangeRequestRepositoryTests`): seeds an `AttendanceRecord` through one `DbContext` instance, then opens a **second** `DbContext` instance bound to the same InMemory database name (not the same tracked instance used to seed — the load-bearing detail that makes this a real proof rather than a vacuous pass against an already-tracked entity), calls `GetTrackedRecordAsync`, asserts `db.Entry(tracked).State == EntityState.Unchanged` (tracked, not detached), mutates a field, calls the repository's own `SaveChangesAsync`, then reloads through a **third** `DbContext` instance with `AsNoTracking()` and asserts the mutation persisted — proving the tracked-fetch path works end-to-end without a blind detached `Update()`. A companion `GetRecordAsync_ReturnsNoTrackingEntity` test proves the read-only counterpart stays detached, per the task's "where useful" allowance, without broadening scope further.

**Stable replacement architecture assertion.** `TimeTrackingMutationArchitectureTests.AttendanceRepository_UsesTrackedFetchForMutation` was replaced with two reflection-based checks that assert only stable structural properties, never source text, method order, or line numbers:
- `AttendanceRepository_ExposesTrackedFetchForMutation` — `IAttendanceReadRepository.GetTrackedRecordAsync` exists, returns `Task<AttendanceRecord>`, and takes `(Guid, Guid, DateOnly, CancellationToken)` (nullable reference annotations are erased at the CLR level, so `Task<AttendanceRecord>` is checked, not `Task<AttendanceRecord?>`).
- `MutationHandlers_DependOnAttendanceRepositoryAbstraction` (a `[Theory]` over `ClockInCommandHandler` and `WorkAreaChangeRequestWorkflow`) — each mutation handler's constructor depends on the `IAttendanceReadRepository` abstraction.

The pre-existing `ApplicationAssembly_DoesNotReferenceEfCore` fact (already covering "Application does not reference EF Core") was left untouched rather than duplicated. `BreakMigration_AddsOnlyTheFilteredUniqueOpenBreakIndex` and `AttendanceMigration_StillEnablesForcedTenantRls` are also source-string-based but were passing and out of scope for this correction, so they were not touched.

**Verification.** `dotnet test tests\ONEVO.Tests.Architecture --configuration Release --no-restore` (full suite): **679 passed, 0 failed** (up from 676 passed/1 failed/677 total in every prior Part's report — net +2 from replacing 1 brittle fact with 1 fact + a 2-case theory).

### Part B — HTTP/PostgreSQL end-to-end runtime test

**New test file.** `tests/ONEVO.Tests.Integration/Features/TimeAttendance/WorkAreaChangeRequestRuntimeHttpIntegrationTests.cs`.

**Fixture and reused infrastructure.** Built directly on the established `AttendanceCorrectionsIntegrationTests` pattern: `E2ETestFactory` (unmodified — no production authentication changes), `WebApplicationFactoryCollection`, `IntegrationTestEnvironmentScope`, `PositionAssignmentRepositoryTestSupport.CreateRepository` (so seeded position assignments go through the real `EfPositionAssignmentRepository.TryCreateActiveAssignmentAsync`, which itself calls `EfEmployeeHierarchyClosureRepository.RebuildAsync` — the same closure-rebuild path production position-assignment endpoints use, rather than hand-authoring closure rows), and the same admin-login/tenant-provisioning/invite-accept/session-exchange boilerplate. `ONEVO_TEST_DB` is honored; otherwise a `postgres:16-alpine` Testcontainer is started per test (Docker was available and used throughout — `docker version` reported server 29.6.2).

Two tenants were provisioned (isolation fixture): tenant A with an approver, three requester employees (one per scenario needing an independent employee/date slot under the partial unique index), and a "wrong approver" employee who legitimately holds `attendance:approve` but is not the resolver-selected route; tenant B with its own approver. Each employee's Position and PositionAssignment (`ReportsToPositionId`/`ReportsToEmployeeId`) route the reporting-line fallback tier of `EmployeeAuthorityResolver.ResolveApproverAsync` to the intended approver — no management-coverage rows were needed for this fixture. `attendance:approve` was granted via real `Role`/`RolePermission`/`UserRole` rows against the permission already seeded by `PermissionSeeder` at host startup (queried by code, not recreated), which the real `PermissionResolver.ResolveAsync` reads via `ListRolePermissionCodesWithModulesAsync` on every request — no permission caching in the cookie itself, so grants seeded any time before a request take effect immediately. The Legal Entity's `WorkStartTime`/`WorkEndTime`/`BreakDurationMinutes`/`Timezone` and a full-company `ClockInPolicy` (Onsite and Remote branches deliberately distinguished on `TrayEnabled`/`PhotoRequired`, both keeping `WebEnabled` true) were seeded directly through the factory's `ApplicationDbContext`, matching the direct-DbContext-seeding convention `AttendanceCorrectionsIntegrationTests` already established for this suite.

`WorkDate` is resolved from the **real** clock (`Asia/Colombo`) inside `InitializeAsync`, not hardcoded, and the Legal Entity's `StandardWorkingDays` was set to include every day of the week — an earlier attempt to pin a fixed `IDateTimeProvider` for full determinism was tried and reverted (see "Blocker investigated and resolved" below) because it broke ASP.NET Core's own real-time cookie/ticket-expiry checks; resolving the work date from the real clock plus an all-days-working-day legal entity gives the same determinism (no dependency on which real weekday the schedule resolver sees) without touching authentication timing.

**Exact end-to-end sequence proven** (`FullLifecycle_SubmitApproveClockInHistory_ReflectsApprovedRemoteOverride`): an unsupported requested work area (`"field"`) is rejected by FluentValidation (400) before any request exists → baseline Today is On-site (`active_employee_work_mode`, Onsite Clock-in Policy branch: web enabled, tray/photo disabled) → preview returns the current/requested areas and the resolved approver's user id → create returns `201` with a pending request (no `tenantId` in the response body) → a second active request for the same employee/date is rejected (409) while the first is pending → Today is still unaffected while pending → the approval inbox contains the request for the correct approver, is empty for the permission-holding-but-wrong-approver reviewer, and returns 403 for the requester (no `attendance:approve`) → the wrong approver's approve attempt is 403 → approve succeeds (200, `reviewedById` = approver's user id) → approving the same request again returns the existing conflict (409) → Today now reports `remote`/`approved_work_area_change_request` and the Remote Clock-in Policy branch (web+tray+photo enabled) → clock-in succeeds, persists `ExpectedWorkArea = "remote"` (verified directly through a scoped `DbContext`), and its own Today-shaped response reports `attendance_record_snapshot` → a subsequent Today call still reports the snapshot → history for the work date shows `expectedWorkMode = "remote"` → database invariants hold: exactly one approved request, exactly one attendance record for that employee/date, and `Employee.WorkModeId` still `1` (onsite) — approval never mutated the permanent work mode.

**Approval-after-clock-in synchronization** (`ApprovalAfterClockIn_SynchronizesExistingAttendanceSnapshot`): a second requester clocks in while still On-site (permanent mode, no override yet) → submits and gets approved a same-date Remote request → the **same** attendance record (same id) is reloaded and its `ExpectedWorkArea` is now `remote`, while `ActualStart` is unchanged, `ActualEnd` is still null, and `AttendanceSource` is still `"web"` → Today and History both immediately reflect Remote from the synchronized snapshot. This is the one path the primary scenario cannot prove (it approves before clock-in).

**State-validation coverage** (`RejectedAndCancelledRequests_DoNotAffectToday`): a rejected request does not change Today; a request is then allowed for the same employee/date once the prior one reached a terminal state; a cancelled request also does not change Today.

**Session/cookie/CSRF behavior** (`Unauthenticated_And_MissingOrInvalidCsrf_AreRejected`): no session cookie → `401` on Today, Clock-in, and Work Area create; a valid session cookie with a missing `X-CSRF-Token` header → `403`; a valid session cookie with an invalid CSRF token value → `403`. The real `CsrfProtectionMiddleware` was exercised unmodified — no CSRF bypass was introduced for tests.

**Permission and selected-approver behavior**: proven inline in the primary scenario — a user with `attendance:approve` who is not the resolver-selected approver for that employee (`wrongApproverA`) cannot see the request in their inbox and gets `403` attempting to approve it; a user without `attendance:approve` (the requester) gets `403` from the approvals endpoints.

**Tenant isolation** (`TenantIsolation_CannotSeeOrApproveAnotherTenantsRequest`): tenant B's approver (who does hold `attendance:approve` in their own tenant) does not see tenant A's request in their approval inbox, and their approve attempt against tenant A's request id returns `404` (the workflow's `GetTrackedByIdAsync(currentUser.TenantId, id, ...)` is tenant-scoped, so a foreign-tenant id simply does not resolve — no RLS bypass, no `IgnoreQueryFilters`). Tenant A's Today state is confirmed unaffected by the failed cross-tenant attempt.

**Blocker investigated and resolved (per Part E instructions, since the environment worked in Part 2's own earlier verification).** The first fixture design pinned a fixed `IDateTimeProvider` (via a custom `WebApplicationFactory` subclass swapping the DI registration) so schedule/working-day resolution would be fully deterministic. This caused every admin/tenant HTTP call after login to fail CSRF validation with a generic "missing or invalid CSRF token" `403`. Root cause, confirmed by direct investigation rather than guessing: `TenantDatabaseTicketStore`/the analogous admin ticket store compute `session.ExpiresAt` using the app's injected `IDateTimeProvider`, but ASP.NET Core's own `CookieAuthenticationHandler` validates ticket freshness against its own real-time clock independent of that provider; pinning the app clock to a fixed instant in the past relative to the machine's real wall-clock time made every ticket appear already expired to ASP.NET Core itself, so `context.AuthenticateAsync(...)` failed before the CSRF hash was ever compared. The fix was to drop the global clock override entirely: `WorkDate` is resolved from the real clock in `InitializeAsync`, and the Legal Entity's `StandardWorkingDays` is configured to include every day of the week, which gives the same determinism (schedule resolution never depends on which real weekday the suite runs on) without touching authentication timing at all. This is recorded here per the instruction to investigate an unexpected environment failure before working around it, since Docker/the rest of the stack were confirmed working throughout.

**Production defect found and fixed.** Running the real ASP.NET host and calling `POST /api/v1/attendance/work-area-change-requests/preview` surfaced `System.InvalidOperationException: Unable to resolve service for type 'ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests.WorkAreaChangeRequestWorkflow' while attempting to activate 'PreviewWorkAreaChangeRequestCommandHandler'` — every Work Area Change Request MediatR handler (`Preview`, `Create`, `Approve`, `Reject`, `Cancel`, `ListMy`, `ListApprovals`) takes `WorkAreaChangeRequestWorkflow` as a constructor dependency, but unlike its sibling `AttendanceCorrectionWorkflow` (registered at `src/ONEVO.Application/DependencyInjection.cs:43` via `services.AddScoped<AttendanceCorrectionWorkflow>()`), `WorkAreaChangeRequestWorkflow` itself was never registered in the DI container anywhere. This is exactly the risk Part 1's Final Hardening section flagged explicitly: *"No test in this pass boots the real ASP.NET host and resolves `WorkAreaChangeRequestWorkflow` through it... This is assessed as low risk given the dependency is already registered and used elsewhere, but it is not empirically proven end-to-end in this pass."* It was not, in fact, registered — every Work Area Change Request HTTP endpoint would have failed with a 500 in a real running application. The smallest correct fix was applied: one line added immediately after the `AttendanceCorrectionWorkflow` registration —

```csharp
services.AddScoped<ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests.WorkAreaChangeRequestWorkflow>();
```

— in `src/ONEVO.Application/DependencyInjection.cs`. No other production behavior was changed. The new HTTP integration test suite is the regression test: every Work Area endpoint is now exercised through the real DI container and real ASP.NET Core pipeline, so a future removal of this registration would fail loudly again.

**Test-data and cleanup.** Unique tenant slugs (`wa-run-a`/`wa-run-b`) and fixture emails scoped to `@wa-run-*.test`; no dependency on the `acme`/`dapi` development seed tenants; Testcontainers instances are disposed in `DisposeAsync`; the `CapturingEmailService` fake avoids real email delivery; no AWS/Cloudflare calls are made; no arbitrary sleeps beyond the existing bounded polling helpers (`WaitForSeedersAsync`, `WaitForInviteTokenForAsync`) already used by this suite's established pattern.

### Verification commands and exact results

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore` | Passed — 0 errors, 0 warnings. |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChange\|FullyQualifiedName~ExpectedWorkArea\|FullyQualifiedName~AttendanceRepository\|FullyQualifiedName~ClockIn\|FullyQualifiedName~AttendanceToday\|FullyQualifiedName~AttendanceRead"` | **134 passed, 0 failed.** |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore` (full suite) | **3204 passed, 0 failed** (+2 over the 3202 recorded in this report's §14, from the two new `EfAttendanceReadRepositoryTests` behavioral tests). |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | **679 passed, 0 failed** (up from 676/677 in every prior Part). |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequestRuntimeHttpIntegrationTests" --logger trx --results-directory TestResults --blame-hang --blame-hang-timeout 10m` | **5 passed, 0 failed, 0 skipped** — real HTTP/PostgreSQL, Docker Testcontainers. |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequestsIntegrationTests\|FullyQualifiedName~ExpectedWorkAreaResolverIntegrationTests"` | **21 passed, 0 failed** (13 + 8, unmodified, re-verified green after the DI fix and architecture-test change). |
| `git diff --check` | Exit code 0. Only pre-existing Windows LF→CRLF advisory warnings; no whitespace errors, no conflict markers. |
| `git status --short` | Unchanged file set beyond the files listed below plus this report; nothing staged. |

### Files changed in this pass

| Area | File | Change |
|---|---|---|
| Architecture test | `tests/ONEVO.Tests.Architecture/TimeTrackingMutationArchitectureTests.cs` | Replaced the brittle source-string-slicing fact with reflection-based structural assertions. |
| Unit test | `tests/ONEVO.Tests.Unit/Features/TimeAttendance/EfAttendanceReadRepositoryTests.cs` | Added the real tracked-fetch-then-mutate-then-reload behavioral test and a no-tracking counterpart. |
| DI registration (production defect fix) | `src/ONEVO.Application/DependencyInjection.cs` | Added the missing `services.AddScoped<WorkAreaChangeRequestWorkflow>()` registration. |
| New integration test | `tests/ONEVO.Tests.Integration/Features/TimeAttendance/WorkAreaChangeRequestRuntimeHttpIntegrationTests.cs` | New — full HTTP/PostgreSQL runtime coverage (5 facts) described above. |
| Report | `WORK_AREA_CHANGE_REQUEST_BACKEND_PART2_RUNTIME_INTEGRATION_REPORT.md` | This section. |

### Skipped or blocked checks

None. Docker was available throughout; the real HTTP/PostgreSQL suite actually ran and passed; the full architecture suite reached zero failures; the existing Work Area PostgreSQL tests were re-verified green.

### Remaining risks

- The negative HTTP/security matrix covers authentication, CSRF, permission, wrong-approver, and tenant isolation, but not every combination the original task enumerated (e.g. a dedicated test asserting a *different* tenant's approver cannot merely *read* an individual request by id — no such single-resource GET endpoint exists on this controller today, only list/approve/reject/cancel, all of which are covered). Nothing was found unproven within the surface that actually exists.
- The fixture's approver-routing setup uses only the reporting-line tier of `EmployeeAuthorityResolver.ResolveApproverAsync` (Position/PositionAssignment with `ReportsToPositionId`/`ReportsToEmployeeId`); position-coverage and department-coverage routing tiers for Work Area specifically are not separately re-proven through HTTP here, since they are already covered by the 28 direct `EmployeeAuthorityResolverTests` in §"Direct EmployeeAuthority tests added" (Part 1 Final Hardening) against the same resolver class the HTTP path calls unmodified.
- The DI-registration defect fixed here was specific to `WorkAreaChangeRequestWorkflow`; this pass did not perform a general audit of every other Application-layer class for the same missing-registration failure mode, though the same class of risk (a class resolvable only through DI, never constructed by hand, with no host-boot test) could in principle recur elsewhere.

### Final verdict

**Release-ready.**

All gate conditions are met: the API build passes; the full architecture suite has zero failures (679/679); the new HTTP/PostgreSQL integration tests actually ran (real Testcontainers PostgreSQL, real ASP.NET Core host) and passed (5/5); the existing Work Area PostgreSQL integration tests pass unmodified (21/21); `git diff --check` passes with only pre-existing line-ending advisories. One genuine pre-existing production defect (missing DI registration for `WorkAreaChangeRequestWorkflow`, which would have 500'd every Work Area Change Request HTTP endpoint) was found and fixed with the smallest correct change, with the new HTTP suite now serving as its regression test.
