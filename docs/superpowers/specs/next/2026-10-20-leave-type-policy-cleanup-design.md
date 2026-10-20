# Leave type & leave policy cleanup — design

Date: 2026-10-20 · Repos: `HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`

## Why

The current Leave Type form and Leave Policy wizard contradict each other. Both own the
same numbers (days per year, notice, max consecutive, carry-forward), several controls
express the same idea four different ways, screens say "days" while the ledger already
stores hours, and policies attach to people only through legal entity — so effectively
every employee can use every leave type. The user called it "complete garbage". The goal
is one clear owner for every setting, hours everywhere, per-leave-type policies assigned
through the org hierarchy, and versioned policies so leave history never contradicts
itself.

## Findings in the current code (for the builder)

- `LeaveType` holds `DefaultDaysPerYear`, `MaxConsecutiveDays`, `MinimumNoticeDays`,
  `CarryForwardAllowed`, `MaxCarryForwardDays`, `CarryForwardExpiryMonths`,
  `ProRataForNewJoiners`, `RequiresApproval`, `Category`. `Category` is only used for
  calendar colours (`LeaveCalendarOptions.TypeCategoryColors`).
- `LeavePolicy` holds `AccrualMethod` (annual/monthly/daily — `daily` is not implemented,
  behaves like annual), `AccrualStart`, `AccrualAfterNMonths`, `ProrationMethod`,
  `ProbationRestriction`, `MinimumTenureMonths`, `FirstYearReducedPercent` — four of these
  all just delay the entitlement start date (`LeaveEntitlementCalculator.ResolveEntitlementStart`).
- `LeavePolicyLeaveType` (policy ↔ many types) stores `AnnualEntitlementDays`,
  `CarryForwardMaxDays`, `CarryForwardExpiryMonths`. If expiry months is null, **all**
  carry is forfeited — opposite of the desired default. `CarryForwardAllowed` on the type
  is ignored.
- Policy → employee resolution is by legal entity only (`LeavePolicyLegalEntity`,
  dictionary keyed by legal entity in `LeaveEntitlementPlanner` and
  `LeaveRequestSubmissionEvaluator`); if two policies cover one entity one silently wins.
- `LeavePolicy.Version` exists but nothing records which version a balance or request used.
- Entitlements (`LeaveEntitlement`, hours) are generated per employee/type/year by
  `GenerateEntitlements` and `LeaveYearEndEntitlementJob`; "monthly" accrual is computed
  up front as a yearly total, nothing credits monthly. Ledger is `LeaveBalanceAudit`.
- Work-day hours come from the legal entity work window (`WorkDayHoursCalculator`).
- Employee org links: `Employee.DepartmentId`, `Employee.LegalEntityId`, primary
  `PositionAssignment` (`AssignmentKind = PrimaryEmployment`, active, effective-dated).
- Permission `leave:create` exists ("Apply for leave on behalf of others"); `leave:manage`
  gates policy/type admin.

## Goals

1. **Leave type = only the kind of leave.** No numbers on it.
2. **One policy = one leave type.** Many policies may exist for the same leave type.
3. **Hours everywhere** in amounts; notice stays in days.
4. **Amount and its timing are one control:** "Gives [N] hours every [month|year]".
5. **Carry forward all by default**, optional cap (hours) and optional expiry (months).
6. **One "when can new joiners use it" control** replaces probation restriction, accrual
   start, minimum tenure, first-year %, and proration method.
7. **Assignment at four levels** — company (legal entity), department, position, employee.
   Resolution is **per leave type**, most specific wins:
   employee → position → department → company. At any single target, at most one policy
   per leave type.
8. **An employee can only request leave types that resolve to a policy for them.**
9. **Versioned policies:** editing saves a new version with an admin-chosen
   "Apply from" date; credits, balances, and requests record the version they were made
   under; old versions are read-only.
10. **Employee detail → Leave**: a user with permission picks a leave type and changes
    the policy for that person (employee-level override), or resets to inherited.

## Non-goals

- Hourly-worked accrual (hours earned per hours worked).
- Leave year other than calendar year.
- Changing the leave request flow, approvals engine, or balance UI beyond showing the
  resolved policy/source and hours wording.
- Admin portal.

## Anti-goals (would make this a failure even if it "works")

- Any number appearing on both the leave type and the policy.
- Two controls that change the same thing.
- "Days" wording for amounts anywhere in the setup screens.
- Editing a policy rewriting past credits/requests.
- Silent conflicts (two policies for the same type at the same target, or one silently
  winning).

## Leave type (after)

Fields: `Name`, `Code` (locked after create), `Description`, `Color` (replaces
`Category`; calendar uses it directly), `IsPaid`, `ApplicableGender` (all/male/female),
`RequiresDocument`, `DocumentRequiredAfterDays` *(see open call below)*,
`AcceptedDocumentTypes`, `IsActive`.

Removed: `Category`, `DefaultDaysPerYear`, `MaxConsecutiveDays`, `MinimumNoticeDays`,
`CarryForwardAllowed`, `MaxCarryForwardDays`, `CarryForwardExpiryMonths`,
`ProRataForNewJoiners`, `RequiresApproval`.

Builder's call (recorded): `DocumentRequiredAfterDays` → rename to
`DocumentRequiredAfterHours` for consistency. Approval is decided by the policy's
approval mode; "no approval" becomes an approval-mode option (`none`).

## Leave policy (after)

`LeavePolicy` (stable identity): `Id`, `LeaveTypeId`, `Name`, `Description`, `IsActive`,
`CurrentVersionId`, timestamps.

`LeavePolicyVersion` (immutable once effective): `Id`, `LeavePolicyId`, `VersionNumber`,
`EffectiveFrom` (date), and all rule fields:

- `AmountHours` (decimal) + `AmountPeriod` (`month` | `year`)
- `CarryForwardCapHours` (null = no cap), `CarryForwardExpiryMonths` (null = never expires)
- `EligibleFrom` (`immediately` | `after_probation` | `after_months`) + `EligibleAfterMonths`
- `MinimumNoticeDays`, `MaxRequestHours` (null = no limit), `MinRequestHours`
- `MaxTeamAbsencePercent` (null = no limit)
- `ApprovalMode` (`any_one` | `all_must_approve` | `in_order` | `none`)
- Blackout periods belong to the version (`LeavePolicyBlackoutPeriod.LeavePolicyVersionId`).

Removed concepts: `AccrualMethod`, `AccrualStart`, `ProrationMethod`,
`ProbationRestriction`, `MinimumTenureMonths`, `FirstYearReducedPercent`, `Country`,
`JobLevel`, `LeavePolicyLeaveType`, `LeavePolicyLegalEntity`, policy-level
`EffectiveFrom`/`Version` (moved to versions).

The version that applies on a given date = the version with the greatest
`EffectiveFrom <= date`. Versions with a future `EffectiveFrom` may be edited or deleted
until they take effect; effective ones are read-only. "Apply from" may not be earlier
than today (prevents rewriting history).

## Assignment

`LeavePolicyAssignment`: `Id`, `LeavePolicyId`, `LeaveTypeId` (denormalised from the
policy, for the uniqueness constraint), `TargetKind` (`legal_entity` | `department` |
`position` | `employee`), `TargetId`, `CreatedAt`, `CreatedBy`.

Unique index on (`TenantId`, `TargetKind`, `TargetId`, `LeaveTypeId`) — enforces "one
policy per leave type per target".

Resolution for employee E and leave type T (as of date D):
1. employee assignment for (E, T)
2. else position assignment for (E's active primary `PositionAssignment` on D, T)
3. else department assignment for (E.DepartmentId, T)
4. else legal-entity assignment for (E.LegalEntityId, T)
5. else none → E cannot request T and gets no entitlement for T.

Resolution returns the policy plus its source level, used by the employee screen.
Assignments are not effective-dated themselves (builder's call: changing an assignment
takes effect from the change; past credits keep their recorded version).

Conflict UX: assigning a policy to a target that already has a different policy for
that leave type returns a conflict listing the existing policy; the UI asks to confirm
and the call is repeated with `replace = true`.

## Crediting & balances

- Each credit writes a `LeaveBalanceAudit` row (`ChangeType = accrual`) carrying
  `LeavePolicyId` and `LeavePolicyVersionId`. `LeaveEntitlement` gets
  `LeavePolicyId`/`LeavePolicyVersionId` of the version last applied.
- **Year period:** full amount credited on 1 Jan (or on the eligibility date for that
  year, pro-rated by remaining calendar months — month of eligibility counts if
  eligibility falls on/before the 15th).
- **Month period:** amount credited on the 1st of each month once eligible (the month
  of eligibility counts if eligibility falls on/before the 15th). Needs a daily
  background job that credits due months idempotently (unique per employee/type/month),
  run per tenant in system mode with per-tenant `SaveChanges` (see existing RLS
  system-mode job conventions).
- **New version mid-year, year period:** on its `EffectiveFrom`, adjust balance by
  `(newAmount − oldAmount) × remainingMonths / 12` (remaining includes the effective
  month), one audit row.
- **New version, month period:** credits on/after `EffectiveFrom` use the new amount.
- **Assignment change mid-year:** builder's call — the employee's new resolved policy
  applies from the next credit; for year-period policies apply the same
  difference-for-remaining-months adjustment. Already-credited hours are never removed.
- **Carry forward at year end:** carry = remaining hours, capped by
  `CarryForwardCapHours` if set; excess forfeited (audit row). If
  `CarryForwardExpiryMonths` set, carried hours expire on 1 Jan + N months (forfeiture
  audit row by the job). Default (both null) = carry everything, never expires.
- Leave requests store `LeavePolicyVersionId` used for validation at submission.

### Calls settled after builder review (designer's calls, user may override)

- **Manual adjustments / generate / recalculate:** the balance becomes ledger-driven.
  `CreateManualEntitlement` becomes a manual `adjustment` ledger row (no policy version,
  reason required) that version/assignment adjustments never touch. `GenerateEntitlements`
  and `RecalculateEntitlement` are rewritten to run the crediting logic for a year
  (idempotent: only missing credits are written), keeping their preview/CSV screens.
- **Sub-departments:** department resolution walks up `ParentDepartmentId`; the nearest
  department with an assignment for the leave type wins (still below position, above
  company).
- **Inactive policies / ended positions:** a policy with assignments cannot be
  deactivated (UI says "unassign first"). An ended/absent primary position simply skips
  the position level and resolution falls through to department/company.
- **Created or assigned mid-year:** year-period → pro-rated credit for the remaining
  months (same rule as assignment change, old amount = 0). Month-period → credits start
  with the next due month; no backfill.
- **Migration of current year:** existing current-year entitlements are kept as an
  opening `adjustment` ledger row; the new rules apply from the next credit after
  migration. No re-crediting of the past.
- **Eligibility dates:** "right away" counts from hire date. "After probation" uses
  `Employee.ProbationEndDate`; if null the employee is treated as past probation.
  "After N months" = hire date + N months.
- **Pending requests:** keep the version stored at submission for validation and
  approval mode; later versions or assignment changes don't re-route them.

### Balance can go negative with approval (user's decision)

- Available balance = hours **credited so far** (+ effective carry) − used − pending.
  Nothing is advanced from future credits.
- An employee **may still submit** a request larger than their available balance. The
  request is flagged `ExceedsBalanceByHours` (stored on the request at submission).
- The approver sees a clear alert on the request (inbox card + details) — "Exceeds
  balance by N h — balance will go negative" — before deciding.
- If approved, the balance goes negative; following credits pay it back automatically
  (credits just add to the ledger). A negative balance carries into the next year as a
  negative carry (cap/expiry don't apply to negative carry) — designer's call.
- The old `LeaveRequestOptions.AllowUnpaidSplitWhenBalanceShort` path is retired —
  shortfall is never silently converted to unpaid (designer's call).
- Out of scope: settling a negative balance when an employee leaves.

## Request validation

`LeaveRequestSubmissionEvaluator` resolves the policy per (employee, leave type, start
date) via the resolver, fails with "no policy" when none, and applies the version's
notice/min/max hours/blackout/team-absence rules. Approval routing reads the version's
`ApprovalMode` (`none` = auto-approve).

## Screens

**Leave types** — create/edit modal reduced to the fields above (colour picker instead of
category). Cards show colour, paid/unpaid, and "N policies".

**Leave policies** — list shows policy name, leave type, amount ("8 h / month"), current
version, assigned-to summary. Wizard, 3 steps:
1. *Basics*: leave type (locked after create), name, description.
2. *Amount*: "Gives [hours] every [month|year]"; carry forward — "Carry over all unused
   hours" (default) with optional cap and optional expiry; "New joiners can use it":
   right away / after probation / after [N] months.
3. *Rules & assignment*: notice (days), shortest and longest request (hours), max team
   absence %, approval mode, blackout dates; **Assign to**: whole company (each legal
   entity as a checkbox; auto-ticked & hidden when the tenant has one), departments
   (multi-select), positions (multi-select). Conflicts surface inline with a confirm.
Edit reuses steps 1–3 (leave type locked) and ends with **Apply from [date]** (default
today) → creates a new version. Policy detail panel shows version history (number,
effective from, who/when, summary of changes) read-only.

**Employee detail → Leave section** — a list of every active leave type the employee
resolves to (plus a "no policy" state for types not covered), each row: leave type,
policy name, source badge (Employee / Position / Department / Company), amount.
Users with `leave:manage` or `leave:create` see **Change**: opens a small popup — leave
type is fixed, pick from policies for that leave type, Save → employee-level assignment.
Rows with an employee-level override show **Reset to inherited**. Uses shared
`app-select` and popup styles.

## Existing data migration

Data is dev/seed only, but the migration must still be lossless-enough:
- Each existing `LeavePolicyLeaveType` row → a new single-type `LeavePolicy` named
  "<old policy name> · <leave type name>", version 1 effective from old `EffectiveFrom`,
  amount = `AnnualEntitlementDays × work-day hours` of the first linked legal entity
  (fallback 8), period = `month` if old `AccrualMethod = monthly` (amount ÷ 12) else
  `year`; other rules copied/converted (days → hours using the same factor).
- Old `LeavePolicyLegalEntity` rows → `legal_entity` assignments (first wins on
  conflict; log the rest).
- Drop removed columns/tables. Update `DapiLeaveSampleSeeder` to the new model.

## API (shape, builder refines)

- Leave types: existing CRUD, slimmed request/response.
- Policies: `GET /leave-policies`, `GET /leave-policies/{id}` (incl. versions &
  assignments), `POST` (creates policy + v1 + assignments), `POST /{id}/versions`
  (apply-from + rules), `DELETE /{id}/versions/{vid}` (future only), `PUT
  /{id}/assignments` (replace set, with `replace` flag for conflicts), clone,
  activate/deactivate.
- Employee: `GET /employees/{id}/leave-policies` (resolved per type with source),
  `PUT /employees/{id}/leave-policies/{leaveTypeId}` (body: policyId),
  `DELETE /employees/{id}/leave-policies/{leaveTypeId}` (reset).

## Left to the builder

Exact migration SQL, DTO names, job scheduling details, spec/test layout, visual polish
within existing shared components.
