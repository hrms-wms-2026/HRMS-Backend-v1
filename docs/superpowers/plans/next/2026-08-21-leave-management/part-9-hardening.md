# Leave Management — Part 9: Hardening (Phase 9 of 10, backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the real gaps found by auditing Phases 0-8 against the architecture skill's NFR checklist — not a rewrite. Three of the five items originally scoped for this phase turned out, on inspection of the actual shipped code, to already be satisfied; this plan fixes the two that aren't and adds regression guards so they stay fixed.

**Architecture:** No new feature code. Task 1 closes a real per-controller architecture-test gap. Task 2 is a coverage measurement pass (tooling already present, never run for Leave). Task 3 adds a permanent regression-guard unit test for a perf concern that audit found was *already* handled correctly in the real code — the test proves it stays that way. Task 4 is a scripted live end-to-end verification. Task 5 corrects this plan folder's own stale assumption about frontend state.

**Tech Stack:** xUnit, ArchUnitNET (already in use), coverlet.collector (already referenced, never invoked for Leave specifically).

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`; live-run scenario: `C:\HR\leave-management-complete.md` §7 (Priya's worked example).

## Audit findings (read this before touching anything — it changes what Task 3 and Task 5 actually do)

This plan folder's `SUMMARY.md` scoped Phase 9 speculatively, before Phases 2-8 existed. Re-checked against the real shipped code on 2026-08-23:

1. **Architecture test coverage** — this repo's convention is one hand-written `{Controller}ArchitectureTests.cs` per controller (reflection-based permission-mapping assertions), *plus* a separate generic `ControllerArchitectureTests.cs` (ArchUnitNET) that blanket-covers Admin/Tenant policy separation for every controller automatically. The generic one already covers `LeaveTypesController` for free. The per-controller one does **not** exist for it — confirmed via `ls tests/ONEVO.Tests.Architecture/LeaveTypesControllerArchitectureTests.cs` (not found), while `LeaveApprovalsControllerArchitectureTests.cs`, `LeaveBalancesControllerArchitectureTests.cs`, `LeaveCalendarControllerArchitectureTests.cs`, `LeaveEntitlementsControllerArchitectureTests.cs`, `LeavePoliciesControllerArchitectureTests.cs`, and `LeaveRequestsControllerArchitectureTests.cs` (which already asserts on `Cancel`, so Phase 6 didn't leave a gap here) all exist. **Real gap: Task 1.**
2. **Coverage vs. 70%+ target** — `coverlet.collector` is referenced in `tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` but no `dotnet-tools.json`/reportgenerator manifest exists anywhere in the repo, and nobody has run a coverage pass scoped to `Leave` specifically. **Real gap, not yet measured either way: Task 2.**
3. **Perf — "`GET /leave/balances` N+1 risk"** — this was a *forward-looking* concern written into this plan folder's `SUMMARY.md` before `ListAllBalancesQueryHandler` existed. Reading the actual shipped handler and `LeaveBalanceMapping.MapAsync` (Phase 3): the policy lookup is already batched — `ListActiveAggregatesByLegalEntityIdsAsync` is called **once** with the distinct set of legal-entity IDs from the result set, then mapped in memory. There is no N+1 here. **Not a gap — Task 3 adds a regression guard instead of a fix, so it stays this way.**
4. **Live-dev-DB run of Priya's worked example (spec §7)** — genuinely not done yet, and now genuinely runnable since Phases 0-7 are live and Phase 8 (year-end job) has a written plan. **Real task: Task 4** (note: the year-end-carry-forward step needs Phase 8 executed first — see that task's own dependency note).
5. **"Retire the 2026-08-17 mocked `LeaveApiService`/fixtures"** — this plan folder's `SUMMARY.md` assumed frontend Phase 1+ had shipped mocked code that needed retiring. It hasn't: `find src -iname "*leave*"` in the frontend repo returns **zero files**. The 2026-08-17 sketch was a design doc only, never implemented — there is nothing to retire. **Not a real task — Task 5 corrects the stale assumption in this plan folder itself rather than doing invented cleanup work.**

---

### Task 1: Add `LeaveTypesControllerArchitectureTests.cs`

**Files:**
- Create: `tests/ONEVO.Tests.Architecture/LeaveTypesControllerArchitectureTests.cs`

Mirrors `LeaveRequestsControllerArchitectureTests.cs`'s exact structure (read that file first if anything below doesn't compile against the real `RequirePermissionAttribute`/`RequireAnyPermissionAttribute` field names — this plan's version was written by reading that file directly, but confirm before assuming drift). `LeaveTypesController`'s actual permissions (`src/ONEVO.Api/Controllers/Tenant/Leave/LeaveTypesController.cs`, verified on disk): `List`→`leave:read`, `Get`→`leave:read`, `Create`→`leave:manage`, `Update`→`leave:manage`, `Deactivate`→`leave:manage`. `UpdateLeaveTypeRequest` has no `Code` property (verified) — the no-TenantId check below also doubles as documentation that Code is genuinely absent from the mutable-fields contract, not just omitted from validation.

- [ ] **Step 1: Write the test**

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Api.Contracts.Leave.Types;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeaveTypesControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(LeaveTypesController);

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var attr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("TenantPolicy", attr!.Policy);
    }

    [Fact]
    public void Actions_UseExpectedPermissions()
    {
        Assert.Equal("leave:read", GetPermission(nameof(LeaveTypesController.List)));
        Assert.Equal("leave:read", GetPermission(nameof(LeaveTypesController.Get)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveTypesController.Create)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveTypesController.Update)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveTypesController.Deactivate)));
    }

    [Fact]
    public void Controller_InjectsIMediatorOnly()
    {
        var constructor = Assert.Single(ControllerType.GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal("IMediator", parameter.ParameterType.Name);
    }

    [Fact]
    public void RequestContracts_DoNotExposeTenantId()
    {
        foreach (var contractType in new[] { typeof(CreateLeaveTypeRequest), typeof(UpdateLeaveTypeRequest) })
        {
            var names = contractType.GetProperties().Select(p => p.Name);
            Assert.DoesNotContain(names, n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    // Code is immutable after create (spec §2.1: "Code cannot be changed after create") — the
    // mutable-fields contract must not even offer it, not just ignore it if sent.
    [Fact]
    public void UpdateLeaveTypeRequest_DoesNotExposeCode()
    {
        var names = typeof(UpdateLeaveTypeRequest).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(names, n => string.Equals(n, "Code", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetPermission(string methodName)
    {
        var method = ControllerType.GetMethod(methodName);
        var attribute = method!.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);
        var field = typeof(RequirePermissionAttribute)
            .GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)field!.GetValue(attribute)!;
    }
}
```

- [ ] **Step 2: Run to verify it passes against the real controller (this is an audit test, not TDD — it should pass immediately if the controller matches what was verified above, and fail loudly if it doesn't)**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter FullyQualifiedName~LeaveTypesControllerArchitectureTests`
Expected: PASS (4/4). If any assertion fails, the controller has drifted from Part 1's shipped state since 2026-08-21 — investigate the drift before changing this test to match, don't just update the assertion to whatever's there.

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Architecture/LeaveTypesControllerArchitectureTests.cs
git commit -m "test(leave): add missing architecture test for LeaveTypesController"
```

---

### Task 2: Coverage measurement for the `Leave` namespace

**Files:** none created — measurement only, this task's output is a decision (pass/fix) for Task 2a.

- [ ] **Step 1: Run the unit suite with coverage collection**

Run:
```bash
dotnet test tests/ONEVO.Tests.Unit --collect:"XPlat Code Coverage" --results-directory ./coverage-results
```
Expected: test run succeeds, a `coverage.cobertura.xml` file appears under `./coverage-results/<guid>/`.

- [ ] **Step 2: Generate a human-readable report**

No `reportgenerator` tool manifest exists in this repo yet — install it locally for this one-off pass rather than adding a permanent dependency without a separate decision to do so:
```bash
dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.* 2>/dev/null || true
reportgenerator -reports:"./coverage-results/**/coverage.cobertura.xml" -targetdir:"./coverage-results/report" -reporttypes:TextSummary
```
Expected: `./coverage-results/report/Summary.txt` is produced.

- [ ] **Step 3: Check Leave-namespace coverage against the 70%+ target**

Run:
```bash
grep -A 3 "ONEVO.Application.Features.Leave" ./coverage-results/report/Summary.txt
```
Expected: a line-coverage percentage per Leave sub-namespace (`Leave.Type`, `Leave.Policy`, `Leave.Entitlement`, `Leave.Request`, `Leave.Approval`, `Leave.Cancellation`, `Leave.Calendar`, `Leave.Balance`, `Leave.BalanceAudit`). Compare each against the architecture skill's 70%+ business-logic target.

- [ ] **Step 3a: If any sub-namespace is below 70%, list the uncovered handlers/helpers and add unit tests for them**

There is no code to write here in advance — this step's content depends entirely on Step 3's real output, which doesn't exist until Step 1-2 run. When you reach this step: for each handler/helper under 70%, write a focused unit test following that sub-namespace's existing test-file pattern (e.g. `CreateLeaveTypeCommandHandlerTests.cs` for `Leave.Type`, `part-8`'s `ListBalanceAuditQueryHandlerTests.cs` for `Leave.BalanceAudit`) covering its main success path and its most obvious failure path (not-found, forbidden, or validation-conflict, whichever the handler actually branches on). Commit one test file at a time, re-running Steps 1-3 after each addition to confirm the number moved.

- [ ] **Step 4: Commit the coverage tooling decision**

If `reportgenerator` proves useful enough to keep permanently (team's call, not this plan's), add it to `.config/dotnet-tools.json` as a local tool in a separate follow-up — out of scope here. This task's own commit is just the resulting test additions from Step 3a, if any:
```bash
git add tests/ONEVO.Tests.Unit
git commit -m "test(leave): close coverage gaps found in Phase 9 audit"
```
(Skip this commit entirely if Step 3 found every sub-namespace already at or above 70% — don't commit an empty change.)

---

### Task 3: Perf regression guard for `ListAllBalancesQueryHandler` (no bug — audit finding confirmed it's already correct)

**Files:**
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Balance/LeaveBalanceMappingPerfTests.cs`

**Files reused, not modified:** `src/ONEVO.Application/Features/Leave/Balance/Helpers/LeaveBalanceMapping.cs` — verified correct (see Audit findings above). This task adds a test proving it, so a future edit can't silently reintroduce an N+1 without a test failing.

- [ ] **Step 1: Write the regression-guard test**

```csharp
using Moq;
using ONEVO.Application.Features.Leave.Balance.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Balance;

public class LeaveBalanceMappingPerfTests
{
    // Regression guard for the Phase 9 audit finding: LeaveBalanceMapping.MapAsync must call
    // ListActiveAggregatesByLegalEntityIdsAsync exactly once per request, batched over every
    // distinct legal entity in the result set — never once per row. If this test ever fails
    // with Times.AtLeastOnce() succeeding but Times.Once() failing, an N+1 has been
    // reintroduced into this mapper.
    [Fact]
    public async Task MapAsync_BatchesPolicyLookup_RegardlessOfRowCount()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var policiesMock = new Mock<ILeavePolicyRepository>();
        policiesMock
            .Setup(p => p.ListActiveAggregatesByLegalEntityIdsAsync(
                tenantId, It.IsAny<Guid[]>(), 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Policy.RepositoryInterfaces.LeavePolicyAggregate>());

        var rows = Enumerable.Range(0, 50).Select(i => new LeaveEntitlementRow(
            new LeaveEntitlement { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = Guid.NewGuid(), LeaveTypeId = leaveTypeId, Year = 2027 },
            $"EMP{i:000}", $"Employee {i}", null, null, legalEntityId, "Acme UK", "Annual Leave", "ANNUAL", 20m
        )).ToList();

        await LeaveBalanceMapping.MapAsync(policiesMock.Object, tenantId, 2027, new DateOnly(2027, 1, 1), rows, CancellationToken.None);

        policiesMock.Verify(p => p.ListActiveAggregatesByLegalEntityIdsAsync(
            tenantId, It.IsAny<Guid[]>(), 2027, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

*(Confirm `LeavePolicyAggregate`'s exact namespace against `ILeavePolicyRepository.cs` before finalizing — it's referenced qualified above as `Policy.RepositoryInterfaces.LeavePolicyAggregate` on the assumption it lives in `ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces`, matching this plan's other Leave repository-interface placements; adjust the `using`/qualification if the real file places it elsewhere.)*

- [ ] **Step 2: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveBalanceMappingPerfTests`
Expected: PASS — proving today's code is already correct, per the audit finding above.

- [ ] **Step 3: Live EXPLAIN ANALYZE check (per architecture skill NFR: "Use EXPLAIN ANALYZE before production release for high-volume queries")**

Against the real local dev DB, with the `acme` tenant seeded to a realistic size (50+ employees, 2+ leave types — seed more via `DevSmokeTestTenantSeeder` if it doesn't already have enough), run:
```sql
EXPLAIN ANALYZE SELECT * FROM leave_entitlements WHERE tenant_id = '<acme-tenant-id>' AND year = 2026;
```
and separately confirm (via `psql` query log or `pg_stat_statements`, whichever this repo's existing perf-check workflow uses) that hitting `GET /api/v1/leave/balances/all?year=2026` issues a small, constant number of queries — not one growing with employee count. Record the result in this task's commit message; no code change expected here since Step 1 already confirmed the batching is correct in code.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Unit/Features/Leave/Balance/LeaveBalanceMappingPerfTests.cs
git commit -m "test(leave): add N+1 regression guard for ListAllBalances policy lookup (audit found no bug, guards against future regression)"
```

---

### Task 4: Live dev-DB run — Priya's worked example end to end

**Files:** none — manual/scripted verification against the real running API and dev DB.

**Depends on:** Phases 0-7 (executed) for everything through cancellation; Phase 8's `part-8-balance-audit-and-year-end.md` executed for the automatic year-end step (step 10 below) — if Phase 8 hasn't been executed yet when this task runs, do step 10 via the existing manual `POST /api/v1/leave/entitlements/generate` for 2027 instead of waiting on the automatic job, and note in the commit message which path was used.

- [ ] **Step 1: Seed / confirm a UK legal entity and an employee hired 2026-07-01, per spec §7**

Use `DevSmokeTestTenantSeeder`'s `acme` tenant; if no employee with a 1 July 2026 UK hire date exists, add one via the existing employee-creation endpoint or seeder extension (out of scope to script here — this is a one-time data setup step, not new product code).

- [ ] **Step 2: `POST /api/v1/leave/types`** — create "Annual Leave": paid, approval required, no document, `defaultDaysPerYear: 20`, `carryForwardAllowed: true`, `maxCarryForwardDays: 5`, `carryForwardExpiryMonths: 3`.
Expected: 200, type created.

- [ ] **Step 3: `POST /api/v1/leave/policies`** — UK policy referencing that type: 20 days, calendar-day proration, carry max 5, expiry 3 months, `maxTeamAbsencePercent: 20`. Assign to the UK legal entity, activate.
Expected: 200, policy active.

- [ ] **Step 4: `POST /api/v1/leave/entitlements/generate`** for year 2026, the UK legal entity.
Expected: one line for Priya, `totalDays` ≈ **10.0** (mid-year hire, calendar-day proration per spec §4's worked formula), `carriedForwardDays` = **0**.

- [ ] **Step 5: `GET /api/v1/leave/balances/my`** as Priya, year 2026.
Expected: Entitled 10, Used 0, Pending 0, Remaining 10.

- [ ] **Step 6: `POST /api/v1/leave/requests`** as Priya — 10-12 April 2026 (Wed-Fri), Annual Leave.
Expected: 200, `totalDays: 3.0`, status `pending`. Warnings present but non-blocking per spec (team-members-off count, any calendar conflict) — confirm the response includes them without rejecting the request.

- [ ] **Step 7: `GET /api/v1/leave/balances/my`** again.
Expected: Remaining **7** (10 − 0 used − 3 pending), Used still **0**.

- [ ] **Step 8: `POST /api/v1/leave/requests/{id}/approve`** as Priya's manager.
Expected: 200, status `approved`. Re-check balances: Used **3**, Remaining **7**. Confirm a `LeaveBalanceAudit` row of `ChangeType: deduction` appears via `GET /api/v1/leave/balance-audit?employeeId={priya}` (Phase 8) with `daysChanged: -3`.

- [ ] **Step 9: `POST /api/v1/leave/requests/{id}/cancel`** as Priya, with `partialCancelEffectiveDate` set to the last day of the approved range (after the leave has started per spec's worked example — adjust the seeded "today" if the dev clock isn't already past the start date, or use a request whose dates are in the recent past for this step specifically).
Expected: 2 days stay used, 1 day restored. Used **2**, Remaining **8**. An `adjustment` audit row appears for `+1` day.

- [ ] **Step 10: Year-end — either the automatic job or the manual generate path (see this task's Depends-on note)**

If Phase 8 executed: wait for/trigger `LeaveYearEndEntitlementJob.RunForYearAsync(2027, ct)` directly (it's public for exactly this kind of manual trigger). If not: `POST /api/v1/leave/entitlements/generate` for 2027, same legal entity.
Expected: Priya's 2027 entitlement — `carriedForwardDays: 5` (capped from her 8 remaining), a `forfeiture` audit row for `-3` days, `totalDays: 20` (full year now, no longer mid-year prorated), so **2027 total = 25**. This must match spec §7 step 10 exactly.

- [ ] **Step 11: Record the result**

If every number above matched, this phase's live-verification exit criterion is met. If any number didn't match, that's a real bug in the corresponding phase's handler — file it against that phase, don't patch it inside this hardening pass without understanding which phase's logic is actually wrong.

- [ ] **Step 12: Commit the verification record**

```bash
git add docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md
git commit -m "docs(leave): record live end-to-end verification of spec §7 worked example"
```
(Update the SUMMARY.md phase table with a note: "Live-verified against spec §7, [date], all balance numbers matched.")

---

### Task 5: Correct this plan folder's stale frontend assumption

**Files:**
- Modify: `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`

- [ ] **Step 1: Replace the Phase 9 frontend bullet**

The current Phase 9 section says "Frontend: retire the 2026-08-17 mocked `LeaveApiService`/fixtures entirely..." — this is stale (see Audit findings above: nothing was ever built, so nothing needs retiring). Replace it with an accurate note:

```markdown
- Frontend: no retirement needed — the 2026-08-17 sketch was design-doc-only and no code from
  it was ever committed (confirmed 2026-08-23: zero Leave-related files in the frontend repo).
  Frontend Phase 1 (`Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-21-leave-management/part-1-leave-types-frontend.md`)
  has not been executed yet — that is a separate, still-pending piece of work, not part of
  this backend hardening phase.
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md
git commit -m "docs(leave): correct stale frontend-retirement assumption in Phase 9 scope"
```

---

### Task 6: Full-suite final run

- [ ] **Step 1: Full unit + architecture + integration suites**

Run:
```bash
dotnet test tests/ONEVO.Tests.Unit
dotnet test tests/ONEVO.Tests.Architecture
dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~Leave
```
Expected: all green, including the new Task 1 and Task 3 tests.

- [ ] **Step 2: Update plan status**

Edit `SUMMARY.md`: mark Phase 9 `**Status:**` as "written in full — **executed [date]**." Update `plans/next/SUMMARY.md` and `plans/SUMMARY.md`.

- [ ] **Step 3: Final commit**

```bash
git add docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md docs/superpowers/plans/next/SUMMARY.md docs/superpowers/plans/SUMMARY.md
git commit -m "docs(leave): mark Phase 9 (backend hardening) executed"
```

---

## What this phase deliberately does not cover

Frontend hardening (retiring nothing, since nothing frontend exists yet), and the frontend build itself (Phases 1-9 on the frontend side) — tracked separately in `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`, still at zero phases executed as of 2026-08-23.
