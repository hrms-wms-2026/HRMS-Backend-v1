# Leave hourly ledger — Part 3: Schema, API, generate, evaluator, migration

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist and return leave **hours**; requests use `StartAt`/`EndAt`; generate `hours = policyDays × workDayHours`; one EF migration backfills existing rows.

**Architecture:** CQRS handlers stay. Swap `LeaveRequestDayCalculator` for Part 2’s hour calculator inside `LeaveRequestSubmissionEvaluator`. Policy/type day fields stay. Ledger columns rename to hours. Depends on Parts 1 and 2.

**Tech Stack:** EF Core, PostgreSQL, MediatR, xUnit.

**Spec:** `docs/superpowers/specs/next/2026-09-09-leave-hourly-ledger-design.md` §§ Ledger vs config, Request model, Generate, Migration, Errors.

**Global constraints:** JSON names camelCase matching properties (`totalHours`, `startAt`). Hour columns `numeric(8,2)`. Policy `annualEntitlementDays` / `minDaysPerRequest` / leave type `defaultDaysPerYear` **do not rename**. Unset work window: generate skip + request 400. Backfill-only 8.0h if window unset.

## Rename map (use this everywhere — do not invent synonyms)

| Old | New |
|---|---|
| `LeaveRequest.StartDate` / `EndDate` / `HalfDayPeriod` | `StartAt` / `EndAt` (`DateTimeOffset`) |
| `LeaveRequest.TotalDays` / `PaidDays` / `UnpaidDays` | `TotalHours` / `PaidHours` / `UnpaidHours` |
| `LeaveEntitlement.TotalDays` / `UsedDays` / `PendingDays` / `CarriedForwardDays` | `TotalHours` / `UsedHours` / `PendingHours` / `CarriedForwardHours` |
| `LeaveBalanceAudit.DaysChanged` | `HoursChanged` |
| `LeaveRequestDayAllocation.DayUnit` / `PaidUnit` / `UnpaidUnit` | `HoursUnit` / `PaidHoursUnit` / `UnpaidHoursUnit` |
| DTO `CurrentRemainingDays` etc. | `CurrentRemainingHours` / `PendingAfterSubmitHours` / `RemainingAfterSubmitHours` |
| API `SubmitLeaveRequestRequest` dates + halfDay | `StartAt` `DateTimeOffset`, `EndAt` `DateTimeOffset`, no half-day |
| Generate line `totalDays` | `totalHours` (and carried/remaining hours) |
| Manual/adjust request `totalDays` | `totalHours` |

Keep table names (`leave_request_day_allocations`). JSON follows new C# names.

---

### Task 1: Domain entities + EF config

**Files:**
- Modify: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequest.cs`
- Modify: `src/ONEVO.Domain/Features/Leave/Entitlement/Entities/LeaveEntitlement.cs`
- Modify: `src/ONEVO.Domain/Features/Leave/BalanceAudit/Entities/LeaveBalanceAudit.cs`
- Modify: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestDayAllocation.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveRequestConfiguration.cs`
- Modify: entitlement + audit EF configs in `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/`

- [ ] **Step 1: Change `LeaveRequest` to:**

```csharp
public DateTimeOffset StartAt { get; set; }
public DateTimeOffset EndAt { get; set; }
public decimal TotalHours { get; set; }
public decimal PaidHours { get; set; }
public decimal UnpaidHours { get; set; }
```

Delete `StartDate`, `EndDate`, `HalfDayPeriod`, `TotalDays`, `PaidDays`, `UnpaidDays`. Keep status, reason, snapshot, notice, cancel fields.

`LeaveEntitlement`: `TotalHours`, `UsedHours`, `PendingHours`, `CarriedForwardHours` (delete the Days twins).

`LeaveBalanceAudit`: `HoursChanged` instead of `DaysChanged`.

`LeaveRequestDayAllocation`: `HoursUnit`, `PaidHoursUnit`, `UnpaidHoursUnit`.

EF: those decimals `HasColumnType("numeric(8,2)")`. Drop `HalfDayPeriod`. Replace start/end date index with `HasIndex(r => new { r.TenantId, r.StartAt, r.EndAt })`.

- [ ] **Step 2: Build the Domain + Infrastructure projects**

```
dotnet build src/ONEVO.Domain
dotnet build src/ONEVO.Infrastructure
```

Expected: FAIL on remaining Days property references — do not “fix” by adding aliases. Proceed to Task 2.

---

### Task 2: DTOs, contracts, messages

**Files:** every Leave request/entitlement/balance DTO under `src/ONEVO.Application/Features/Leave/**/DTOs/` and `src/ONEVO.Api/Contracts/Leave/`.

`SubmitLeaveRequestRequest` and on-behalf twin become:

```csharp
public sealed record SubmitLeaveRequestRequest(
    Guid LeaveTypeId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason,
    IReadOnlyList<Guid>? FileRecordIds);
```

`LeaveRequestResponse` / list item: `StartAt`, `EndAt`, `TotalHours`, `PaidHours`, `UnpaidHours` — no `HalfDayPeriod`. Balance impact hours names from the map.

Generate/manual/adjust DTOs: `TotalHours` / `CarriedForwardHours` / `RemainingHours`. Audit list: `HoursChanged`.

`LeaveRequestMessages.HalfDaySameDay` — delete. Add `LeaveRequestMessages.WorkWindowRequired = "Set work start and end in General Settings first."` and `EndAtNotAfterStartAt`.

Grep `HalfDayPeriod`, `TotalDays`, `startDate` (leave only), `remainingDays` under Application + Api + tests and apply the map. Do not rename policy `AnnualEntitlementDays` or type `DefaultDaysPerYear`. Also grep the rest of the backend for `LeaveRequest.HalfDayPeriod` / `StartDate` (attendance leave-aware included) so the solution still compiles; map any AM/PM half-day attendance flag to “any overlapping leave hours that calendar date”.

---

### Task 3: Evaluator + generate use hours

**Files:**
- Modify: `src/ONEVO.Application/Features/Leave/Request/Services/LeaveRequestSubmissionEvaluator.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementPlanner.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Cancellation/Helpers/LeaveRequestDayAllocationBuilder.cs`
- Modify: year-end job `src/ONEVO.Infrastructure/Services/Leave/LeaveYearEndEntitlementJob.cs`

- [ ] **Step 1: Evaluator**

Load the employee’s legal entity `WorkStartTime` / `WorkEndTime` / `BreakDurationMinutes` / `Timezone` / `StandardWorkingDays`. If start or end is null, return `Result.Failure(LeaveRequestMessages.WorkWindowRequired)` (HTTP 400).

Convert `StartAt`/`EndAt` to that timezone, then call `LeaveRequestHourCalculator`. If `TotalHours == 0`, 400 (weekend/holiday-only). If `TotalHours < policy.MinDaysPerRequest * workDayHours`, 400.

Replace remaining-days paid/unpaid split with remaining-**hours** (`entitlement.TotalHours + CarriedForwardHours - UsedHours - PendingHours`). Pending reservation uses `PaidHours`.

Delete `HalfDaySameDay` check.

- [ ] **Step 2: Generate**

After `LeaveEntitlementCalculator.Calculate` (still returns **days** for the annual portion — do not change that helper’s unit), convert:

```csharp
var workHours = WorkDayHoursCalculator.TryCompute(assignment.WorkStartTime, assignment.WorkEndTime, assignment.BreakDurationMinutes);
if (workHours is null or <= 0)
{
    skipped.Add(new(..., "Set work start and end in General Settings first."));
    continue;
}
var totalHours = decimal.Round(calculation.TotalDays * workHours.Value, 2, MidpointRounding.AwayFromZero);
var carryHours = decimal.Round(calculation.CarriedForwardDays * workHours.Value, 2, MidpointRounding.AwayFromZero);
```

Planner line DTOs carry hours. Manual create/adjust APIs accept hours and write hours. Recalculate: re-run days calculator then × current `workDayHours`.

- [ ] **Step 3: Day allocation builder**

Each counted shift-start date gets `HoursUnit` from that day’s overlap (full → `workDayHours`, partial → overlap). Do not leave `DayUnit` around.

---

### Task 4: Migration + backfill

**Files:** new EF migration under `src/ONEVO.Infrastructure/Persistence/Migrations/`.

- [ ] **Step 1: Add migration after entities compile**

```
dotnet ef migrations add LeaveHourlyLedger --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Edit the `Up` to:

1. Add `start_at` / `end_at` timestamptz, hour columns `numeric(8,2)`.
2. Backfill SQL (window unset → 8.0 **only here**):

```sql
-- entitlements / audit: days * work_day_hours
-- requests: start_at = start_date at work_start (entity tz), end_at = end_date at work_end
-- AM: start_at = work_start, end_at = start_at + (work_day_hours/2)
-- PM: end_at = shift end, start_at = end_at - (work_day_hours/2)
-- allocations: day_unit * work_day_hours → hours_unit
```

Join `legal_entities` on the employee’s `legal_entity_id`. Overnight: if `work_end <= work_start`, end timestamp is next calendar date.

3. Drop `half_day_period`, old date and day columns.
4. Recreate indexes on `start_at`/`end_at`.

- [ ] **Step 2: Apply on the test/dev database the integration tests use**

```
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

---

### Task 5: Tests compile and key cases pass

Grep tests for `HalfDayPeriod`, `TotalDays`, `StartDate` on leave types (not policy annual days). Update fixtures to `StartAt`/`EndAt`/`TotalHours`.

Must-pass filters:

```
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveRequestHourCalculatorTests
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveRequestSubmissionEvaluatorTests
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~GenerateEntitlementsCommandHandlerTests
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveEntitlementCalculatorTests
dotnet test tests/ONEVO.Tests.Architecture --filter FullyQualifiedName~Leave
```

Add/adjust:

- Generate 14 days × 8h window → 112.00 hours; skip when window unset.
- Submit without window → 400 `WorkWindowRequired`.
- Preview afternoon 14:00–18:00 → `totalHours` 4.00.

`LeaveEntitlementCalculatorTests` stay in **days** (policy math unchanged).

- [ ] **Commit**

```
git add src tests
git commit -m "feat(leave): store and return hours; StartAt/EndAt; migrate day rows"
```
