# Leave Policy Cleanup — Part 2: Crediting, Balances, Requests (backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Balances are credited from the resolved policy version ("N hours every month/year"), carry everything forward by default, can go negative when an approver accepts an over-balance request, and every credit and request records its policy version.

**Architecture:** A pure `LeaveCreditCalculator` computes, for one employee / leave type / year, the hours that *should* have been credited by a date, valuing each calendar month by the policy version that applied to it. A `LeaveCreditService` compares that with the hours already credited in the ledger (`LeaveBalanceAudit`) and writes the difference as one row — idempotent, so the daily job, "Generate", "Recalculate" and policy edits all call the same thing. Requests resolve their policy through Part 1's `ILeavePolicyResolver`.

**Tech Stack:** as Part 1.

**Spec:** `docs/superpowers/specs/next/2026-10-20-leave-type-policy-cleanup-design.md`. **Prerequisite:** Part 1 complete (`part-1-model-versions-assignments.md`) — uses `ILeavePolicyResolver`, `ResolvedLeavePolicy`, `LeavePolicyVersion`, vocabularies.

## Global Constraints

- Everything in Part 1's Global Constraints.
- Remove every Part 1 stub: at the end `grep -rn "Leave cleanup Part 2" src tests` returns nothing and no test is skipped for that reason.
- Leave year = calendar year. Eligibility date: `immediately` → hire date; `after_probation` → `ProbationEndDate + 1 day` (null → hire date); `after_months` → hire date + N months.
- A month **counts** for an employee if their eligibility date is on or before the 15th of that month (or earlier). Months before that don't count.
- Year period: all counting months of the year are credited at once on 1 Jan (or on the eligibility date if later), each worth `AmountHours / 12`.
- Month period: each counting month is credited on its 1st (or on the eligibility date, for the eligibility month), worth `AmountHours`.
- **Credits are attributed to months.** Every accrual ledger row carries `Year`, `CoversFromMonth`, `CoversToMonth` (1–12); its hours are spread evenly over those months. "Credited for month m" = Σ over accrual rows covering m of `HoursChanged / (CoversTo − CoversFrom + 1)`.
- **Past months are frozen.** A month `m < AsOf.Month` that already has any credit coverage is never re-valued. A past month with **no** coverage (job was down, employee newly eligible) is credited once, valued by the policy resolved on that month's credit date — because assignments and versions are dated (Part 1), a policy assigned in October resolves to nothing for January, so there is no backfill.
- **Current and future months are re-valued.** Month-period: the current month (once due) is valued by the policy resolved on its credit date. Year-period: the current and remaining months are valued by the policy resolved on `AsOf`. So a new version or a new assignment changes only the current month onward — the balance can go up **or down** (designer's call, recorded in the spec).
- Known limitation (accepted): `Employee.DepartmentId` has no history, so catching up an *uncovered* past month uses the current department.
- Rounding: 2 decimal places, `MidpointRounding.AwayFromZero`, applied to each written ledger delta only.
- Carry at year end: remaining = credited + effective carry − used (pending stays pending). Positive remaining is capped by `CarryForwardCapHours` (excess → `forfeiture` row); negative remaining carries in full with no cap/expiry. Expiry: carried positive hours expire on 1 Jan + `CarryForwardExpiryMonths` (forfeiture row on that day).
- Only accrual rows **with** `CoversFromMonth` count as policy credits. `adjustment` rows (request deductions/cancellations, manual adjustments) and month-less accrual rows (manual entitlements) are never counted or touched by crediting. If the year's `LeaveEntitlement.Source = manual`, crediting skips that employee/type (`SkipReason = "Manual entitlement"`) — same as today's Generate.
- `LeaveRequestOptions.AllowUnpaidSplitWhenBalanceShort` is deleted (and from both appsettings files).

## Review Focus

- **Running the credit service twice on the same day** (job + manual Generate) writes nothing the second time and never double-credits under concurrency. Test in Task 3.
- **Employee hired on the 16th vs the 15th** — month-counting boundary. Test in Task 2.
- **Policy version lowered mid-year** — balance drops by the remaining-months difference, not below what the rules say; used hours untouched. Test in Task 2/3.
- **Over-balance request on an unpaid leave type** — never flagged (unpaid types don't check balance). Test in Task 5.
- **Employee with no resolved policy for a type but an old entitlement row** — balance still shown, request rejected with "no policy". Test in Task 5.

---

### Task 1: Ledger and entitlement columns + spec note

**Files:**
- Modify: `src/ONEVO.Domain/Features/Leave/BalanceAudit/Entities/LeaveBalanceAudit.cs` — add `Guid? LeavePolicyId`, `Guid? LeavePolicyVersionId`, `int? Year`, `int? CoversFromMonth`, `int? CoversToMonth` (check constraint 1–12, from ≤ to, both null or both set).
- Modify: `src/ONEVO.Domain/Features/Leave/Entitlement/Entities/LeaveEntitlement.cs` — add `Guid? LeavePolicyId`, `Guid? LeavePolicyVersionId`, `uint xmin` concurrency token configured like other entities that use one (search `IsRowVersion()`/`xmin` in Configurations).
- Modify: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequest.cs` — add `Guid? LeavePolicyVersionId`, `decimal ExceedsBalanceByHours` (default 0).
- Modify: the three EF configurations; create migration `LeaveLedgerPolicyVersion`. In `Up`, mark today's balances as already credited so the first credit run writes nothing extra: for every `leave_entitlements` row with `source = 'auto'`, set its year's existing `accrual` audit rows (the ones today's Generate wrote) to `year = <entitlement year>`, `covers_from_month = 1`, `covers_to_month = 12`, and `leave_policy_id`/`leave_policy_version_id` = the migrated v1 of the policy now resolving for that employee's legal entity and leave type (null if none). If an auto entitlement has `total_hours` but no accrual row, insert one accrual row with those values and `reason = 'Opening balance (policy cleanup)'`. Leave manual-source entitlements and all non-accrual rows untouched.
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/EfLeaveEntitlementRepositoryTests.cs` or nearest existing repository test — `Concurrent_update_throws_DbUpdateConcurrencyException`; audit configuration test `Covers_months_must_be_both_set_or_both_null`.

- [ ] **Step 1:** write the tests. **Step 2:** run — fail. **Step 3:** implement + migration, `dotnet ef database update`. **Step 4:** run — PASS. **Step 5:** commit `feat(leave): ledger rows and requests carry policy version`.

### Task 2: Credit calculator (pure)

**Files:**
- Delete: `src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementCalculator.cs` and its tests (replaced).
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveCreditCalculator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveCreditCalculatorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record LeaveMonthPolicy(int Month, LeavePolicyVersion Version, Guid PolicyId); // Month 1..12
  public sealed record LeaveCreditInput(int Year, DateOnly HireDate, DateOnly? ProbationEndDate, DateOnly AsOf,
      IReadOnlyList<LeaveMonthPolicy> MonthPolicies); // months with no policy are simply absent
  public sealed record LeaveMonthValue(int Month, decimal Hours, Guid PolicyId, Guid VersionId, DateOnly CreditDate);
  public static class LeaveCreditCalculator
  {
      public static DateOnly EligibilityDate(LeavePolicyVersion version, DateOnly hireDate, DateOnly? probationEndDate);
      // Months that are due as of AsOf, each with its value. Unrounded.
      public static IReadOnlyList<LeaveMonthValue> DueMonths(LeaveCreditInput input);
  }
  ```
  The caller (Task 3) builds `MonthPolicies` per the Global Constraints resolution rule (past uncovered month → resolve on its credit date; current/future → as described there). Eligibility is evaluated per month with that month's version (its `EligibleFrom`). Due = month-period: `CreditDate <= AsOf`; year-period: all counting months once the year's credit date (1 Jan, or eligibility date if later) `<= AsOf`.

- [ ] **Step 1: Failing tests** (all `Year = 2026`, hire 2025-01-01 unless stated; "→ N" means Σ `Hours` of `DueMonths` = N):
  - `Year_period_full_year_on_jan_1` — 112 h/year, asOf 2026-01-01 → 112.
  - `Month_period_credits_only_reached_months` — 8 h/month, asOf 2026-03-01 → 24; asOf 2026-02-28 → 16.
  - `Hire_on_15th_counts_month` — hire 2026-04-15, 8 h/month, asOf 2026-04-15 → 8; `Hire_on_16th_skips_month` — hire 2026-04-16, asOf 2026-04-30 → 0, asOf 2026-05-01 → 8.
  - `Year_period_mid_year_joiner_prorated` — hire 2026-07-10, 120 h/year, asOf 2026-07-10 → 60 (Jul–Dec = 6 months).
  - `After_probation_delays_eligibility` — hire 2026-01-01, probation end 2026-03-31, 8 h/month, asOf 2026-12-31 → 72 (Apr–Dec).
  - `After_probation_null_end_treated_as_hire` — → 96.
  - `After_months` — hire 2026-01-01, after 3 months, 8 h/month, asOf 2026-12-31 → 72.
  - `Year_period_version_raised_in_november` — Jan–Oct v1 120 h/year, Nov–Dec v2 180 h/year → 120×10/12 + 180×2/12 = 130.
  - `Year_period_version_lowered` — Jan–Oct 120, Nov–Dec 60 → 110.
  - `Month_without_policy_contributes_nothing` — policy only Jun–Dec at 8 h/month, asOf 2026-12-31 → 56.
  - `Future_hire_returns_zero`.
- [ ] **Step 2:** run — fail. **Step 3:** implement (pure, no I/O). **Step 4:** run — PASS. **Step 5:** commit `feat(leave): month-sliced leave credit calculator`.

### Task 3: Credit service + year-end carry

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Services/ILeaveCreditService.cs`, `LeaveCreditService.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Entitlement/RepositoryInterfaces/ILeaveEntitlementRepository.cs` + `EfLeaveEntitlementRepository.cs` (add the reads/writes below), `ILeaveBalanceAuditRepository` if writes live there.
- Delete: `src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementPlanner.cs` (replaced; keep `LeaveEntitlementGenerationCsvBuilder`).
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveCreditServiceTests.cs`

**Interfaces:**
- Consumes: `ILeavePolicyResolver.ResolveForEmployeesAsync` (Part 1), `LeaveCreditCalculator` (Task 2).
- Produces:
  ```csharp
  public sealed record LeaveCreditOutcome(Guid EmployeeId, Guid LeaveTypeId, decimal HoursWritten, decimal ExpectedCreditedHours, string? SkipReason);
  public interface ILeaveCreditService
  {
      // Writes missing credits for the year up to asOf. dryRun = compute only (used by Preview).
      Task<IReadOnlyList<LeaveCreditOutcome>> CreditAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, int year, DateOnly asOf, bool dryRun, Guid? actorId, CancellationToken ct);
      // Closes `year`: writes carry_forward into year+1 and forfeiture rows; idempotent per (employee, type, year).
      Task CloseYearAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, int year, Guid? actorId, CancellationToken ct);
      // Writes forfeiture rows for carried hours whose expiry date is <= asOf; idempotent.
      Task ExpireCarryAsync(Guid tenantId, int year, DateOnly asOf, CancellationToken ct);
  }
  ```
  `CreditAsync`, per (employee, leave type): load the year's month-attributed accrual rows → per-month credited amounts. For each due month from `DueMonths`: skip if frozen (month < `asOf.Month` and already covered); otherwise delta_m = value − credited_m. Group consecutive months with the same version and same sign into one ledger row (`ChangeType = accrual`, `HoursChanged` = round(Σ delta), `CoversFromMonth`/`CoversToMonth`, `Year`, policy/version ids, `BalanceAfter`, `Reason` = `"Credit for <Mon yyyy>"` / `"Yearly credit"` / `"Policy change: <policy> v<n>"` when re-valuing already-covered months). Skip rows whose rounded hours are 0. Update `LeaveEntitlement` (TotalHours += Σ, policy/version ids of the current month's value). Save per tenant once; on `DbUpdateConcurrencyException` reload and retry once.
  `SkipReason` values (reuse/extend `LeaveEntitlementMessages`): `"No hire date"`, `"No policy assigned"`, `"Not eligible yet"`, `"Manual entitlement"`.

- [ ] **Step 1: Failing tests** (fake repositories + fake resolver honouring assignment/version dates): `Second_run_same_day_writes_nothing`; `Override_added_in_october_leaves_jan_sep_unchanged` (month-period 8 h → override 12 h from 2026-10-15: run 2026-10-20 writes nothing — October was valued on its 1 Oct credit date; run 2026-11-01 credits November at 12; Jan–Oct total stays 80); `Existing_policy_assigned_in_october_does_not_backfill` (year-period 120 h assigned to department from 2026-10-01, run 2026-10-01: credits Oct–Dec = 30 only); `Department_move_in_july_leaves_jan_jun_unchanged` (Jan–Jun covered under old department policy; employee now in a department with a different policy; run 2026-07-05: Jan–Jun untouched); `Missed_job_days_catch_up_uncovered_past_month` (no row for September, run 2026-10-02: September credited at its own version); `Post_migration_first_credit_writes_nothing_until_next_month` (migrated rows cover 1–12 for an auto entitlement: run 2026-10-21 writes nothing for year-period; for month-period also nothing because all months are covered); `Manual_source_entitlement_is_skipped`; `Version_raise_writes_positive_delta_with_version_id`; `Version_lower_writes_negative_delta_used_hours_unchanged`; `Manual_adjustment_rows_are_not_counted_as_credited`; `Dry_run_writes_nothing_but_returns_expected`; `Close_year_caps_positive_carry_and_forfeits_excess` (remaining 50, cap 40 → carry 40, forfeit 10); `Close_year_without_cap_carries_all`; `Close_year_carries_negative_in_full`; `Close_year_twice_is_idempotent`; `Expire_carry_forfeits_on_expiry_date_only_once`.
- [ ] **Step 2:** run — fail. **Step 3:** implement. **Step 4:** run — PASS. **Step 5:** commit `feat(leave): idempotent credit service and year close`.

### Task 4: Daily job + entitlement commands rewired

**Files:**
- Replace: `src/ONEVO.Infrastructure/Services/Leave/LeaveYearEndEntitlementJob.cs` → `LeaveCreditJob.cs` (rename class + hosted-service registration). Hourly check, runs once per UTC day after 01:00: for each active tenant (same admin-mode enumeration + per-tenant `SwitchToTenantAsync` + **per-tenant save** as the current job): on 1 Jan first `CloseYearAsync(year − 1)`; then `ExpireCarryAsync(year, today)`; then `CreditAsync(all active employees, year, today)`.
- Rewrite handlers to call `ILeaveCreditService`: `GenerateEntitlements` (`CreditAsync`, dryRun false), `PreviewGenerateEntitlements` (dryRun true; CSV/response shape unchanged except "days" → "hours" labels), `RecalculateEntitlement` (single employee, all types, `CreditAsync`). `CreateManualEntitlement` and `AdjustEntitlement` write `adjustment` rows with no policy ids (reason required — keep current validation).
- Test: update `PreviewGenerateEntitlementsQueryHandlerTests`, generate/recalculate handler tests; add `LeaveCreditJobTests` only if the existing job had tests (match precedent).

- [ ] **Step 1:** update tests to assert the handlers delegate to `ILeaveCreditService` with the right year/asOf/dryRun. **Step 2:** run — fail. **Step 3:** implement. **Step 4:** run leave filter — PASS. **Step 5:** commit `feat(leave): daily credit job and entitlement commands use credit service`.

### Task 5: Request submission, approval mode, over-balance

**Files:**
- Modify: `src/ONEVO.Application/Features/Leave/Request/Services/LeaveRequestSubmissionEvaluator.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Request/Commands/SubmitLeaveRequest/SubmitLeaveRequestCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Request/Options/LeaveRequestOptions.cs`, `src/ONEVO.Api/appsettings.json`, `appsettings.Development.json` (delete the option)
- Modify: `ILeaveApprovalRepository` / `EfLeaveApprovalRepository` — approval mode for a request = the stored `LeavePolicyVersionId`'s `ApprovalMode` (fallback: resolve on the request start date for legacy rows with null version id).
- Modify: `CreateApproverBackdatedLeaveCommand` path to resolve + store version the same way.
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestSubmissionEvaluatorTests.cs` (+ submit handler tests)

**Interfaces:**
- Consumes: `ILeavePolicyResolver.ResolveAsync(tenantId, employeeId, leaveTypeId, startDate)`.
- Produces: `LeaveRequestEvaluation` gains `Guid PolicyVersionId`, `string ApprovalMode`, `decimal ExceedsBalanceByHours`; loses the policy aggregate.

Rules (evaluator): no resolved policy → existing `LeaveRequestMessages.NoEntitlement` (rename message text to "No leave policy covers this leave type for you."); gender restriction from leave type (unchanged); `TotalHours < MinRequestHours` → reject `"Leave request must be at least {x} hours."`; `MaxRequestHours` set and `TotalHours > MaxRequestHours` → reject `"Leave request can be at most {x} hours."`; notice uses `Version.MinimumNoticeDays` only (keeps the existing `NoticePeriodMissed` behaviour); blackout from resolved blackouts; team absence from `Version.MaxTeamAbsencePercent`. Paid types: `PaidHours = TotalHours`, `UnpaidHours = 0`, `ExceedsBalanceByHours = max(0, TotalHours − available)` where available = `LeaveEntitlementMapper.Remaining(...)` (missing entitlement row = 0 available, **not** a rejection). Unpaid types: `PaidHours = 0`, `UnpaidHours = TotalHours`, never exceeds.
Submit handler: stores `LeavePolicyVersionId`, `ExceedsBalanceByHours`; when `ApprovalMode == "none"` the request is created already approved — reuse the code path the decision service uses to finalise an approval (used hours move from pending to used, day allocations active, outbox events identical to a normal approval) instead of duplicating it; no approver rows.

- [ ] **Step 1: Failing tests:** `No_policy_rejects`; `Min_request_hours_enforced`; `Max_request_hours_enforced`; `Over_balance_paid_request_is_allowed_and_flagged` (available 16, request 40 → exceeds 24); `Missing_entitlement_row_counts_as_zero_available`; `Unpaid_type_never_flagged`; `Approval_mode_none_auto_approves_and_moves_hours_to_used`; `Approved_over_balance_makes_remaining_negative`; `Pending_request_keeps_stored_version_after_new_version` (approval mode read from stored version).
- [ ] **Step 2:** run — fail. **Step 3:** implement. **Step 4:** run leave filter — PASS and no remaining stubs (`grep -rn "Leave cleanup Part 2" src tests` empty). **Step 5:** commit `feat(leave): requests resolve policy version, allow over-balance with flag`.

### Task 6: Approver alert + balance reads

**Files:**
- Modify: `src/ONEVO.Application/Features/Leave/Inbox/LeaveInboxSourceProvider.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Balance/Queries/**` (GetMyBalances, ListTeamBalances, ListAllBalances, GetEmployeeTimeOff) and `LeaveEntitlementMapper.ToBalance`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Inbox/*`, `tests/ONEVO.Tests.Unit/Features/Leave/Balance/*`

- [ ] **Step 1: Failing tests:**
  - Inbox: pending request with `ExceedsBalanceByHours = 24` → item `Severity = "warning"`; detail `Facts` contains `new InboxFact("Over balance", "24 h — balance goes negative if approved")`; not pending or 0 → no fact, severity unchanged. Keep the "Balance" change row; remove the "Unpaid" row when the leave type is paid and unpaid hours are 0.
  - Balances: a leave type the employee resolves to but has no entitlement row yet appears with 0; a type with an entitlement row but no resolved policy still appears; negative remaining is returned as a negative number; each balance row carries `PolicyName` and `AmountHours`/`AmountPeriod` (add to `LeaveBalanceResponse`).
- [ ] **Step 2:** run — fail. **Step 3:** implement. **Step 4:** run full unit suite `dotnet test tests/ONEVO.Tests.Unit` — PASS. **Step 5:** commit `feat(leave): approver over-balance alert and policy-aware balances`.

### Task 7: Live API check

- [ ] Kill `ONEVO.Api.exe`, start the API (preview server per `.claude/launch.json`; confirm :7229 is listening before calling it), sign in to the Dapi dev tenant and call: create policy (Annual, 8 h/month, assign whole company) → `GET employees/{id}/leave-policies` shows Annual from Company → `PUT` an employee override → row shows Employee source → `POST …/versions` apply-from tomorrow → `GET` shows v2 `IsFuture = true` → run Generate for the year → balance shows credited hours → submit a request larger than the balance → inbox item has the warning fact. Paste the actual responses in the task report; any failure is a bug to fix before continuing.
- [ ] Commit nothing here unless fixes were needed.
