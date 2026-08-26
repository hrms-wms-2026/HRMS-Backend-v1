# Leave Management (Backend) — Phase Index

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`
**Companion (frontend):** `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`
**Source of truth for product behaviour:** `C:\HR\leave-management-complete.md`

This is a 10-phase build (0-9), one deliverable slice per phase, backend and frontend paired where a screen exists. Written in full for Phase 0+1 (`part-1-schema-and-leave-types.md`), Phase 2 (`part-2-leave-policies.md`), Phase 3 (`part-3-entitlements-and-balances.md`), Phase 4 (`part-4-request-submission.md`), Phase 5 (`part-5-approval-workflow.md`), Phase 6 (`part-6-cancellation.md`), Phase 7 (`part-7-team-calendar.md`), Phase 8 (`part-8-balance-audit-and-year-end.md`), and Phase 9 (`part-9-hardening.md`) — all 10 phases now have a written plan. Phase 8 executed 2026-08-23; Phase 9 executed 2026-08-24 (hardening tests shipped; live Priya HTTP e2e still blocked — Docker engine down, local OnevoDb schema behind this branch).

**Every phase's exit criteria includes a live run against the real dev DB** (`DevSmokeTestTenantSeeder`'s acme/dapi tenants), not just a green test suite — see the design doc's Testing note.

---

## Phase 0 — Schema + role-template fix (backend only)

**Status:** written in full — **executed 2026-08-21**, schema + RLS + `leave:manage` role-template/smoke-seeder patch shipped on `feat/leave-management-schema-and-types`.

- All 5+ entities (`LeaveType`, `LeavePolicy` + 3 child tables, `LeaveEntitlement`, `LeaveRequest` + 3 child tables, `LeaveBalanceAudit`) and the 7 string-constant vocabulary classes, per the design doc.
- One EF migration creating all tables.
- Data-patch migration adding `leave:manage` to the `HR Manager` role template's existing seeded row (see design doc's "Real gap found").
- **Depends on:** nothing.
- **Blocks:** every other phase.

## Phase 1 — Leave Types (Screen 1)

**Status:** written in full — **executed 2026-08-21**, Leave Types CRUD (`/api/v1/leave/types`) shipped on `feat/leave-management-schema-and-types`. Frontend companion is still pending.

- Backend: `LeaveTypesController` (`/api/v1/leave/types`) — List, Get, Create, Update, Deactivate. `leave:manage` for writes, `leave:read` or `leave:manage` for list/get (HR config screen only in this phase — the employee-facing type dropdown with per-type remaining days is Phase 4's concern, not this one).
- Frontend: `modules/leave/` scaffolded — `leave-type.model.ts`, `leave-type-api.service.ts`, `leave-type.store.ts`, `leave-types-management` feature page, `leave-type-list-table` + `leave-type-form-modal` ui components, wired to `/time-off/types`.
- **Depends on:** Phase 0.
- **Blocks:** Phase 2 (a policy must reference an existing, active leave type).

## Phase 2 — Leave Policies (Screen 2)

**Status:** written in full — **executed 2026-08-21**, Leave Policies CRUD (`/api/v1/leave/policies` list/get/create/clone + replace-confirm) shipped on `feat/leave-management-part-2`. Frontend companion is still pending. Business values are explicitly request/config driven; no production handler defaults for country, days, dates, percentages, approval mode, or policy limits.

- Backend: `LeavePoliciesController` (`/api/v1/leave/policies`) — List, Get, Create (multi-type + blackout periods + legal-entity assignment + activate/replace-confirmation), Clone. Validate: monthly accrual × 12 ≤ type's annual limit; one active policy per legal entity (replace-confirm flow, spec Screen 2 errors table).
- Frontend: `leave-policy.model.ts`, `leave-policy-api.service.ts`, `leave-policy.store.ts`, `policy-management` feature (multi-step form matching Screen 2's 7 steps), wired to `/time-off/policies`.
- **Depends on:** Phase 1 (leave type picker), `org:read` (legal entity picker, existing).
- **Blocks:** Phase 3.

## Phase 3 — Entitlements + Balance screens (Screens 3 & 4)

**Status:** written in full — **executed 2026-08-21** on `feat/leave-management-part-3` (unit + architecture verified). Live dev-DB smoke and Testcontainers integration pending Docker engine. Business values are request/policy/config driven; calendar proration is inclusive days / 365; carry-forward expiry is applied on read and generate.

- Backend: `LeaveEntitlementsController` (`/api/v1/leave/entitlements`) — bulk generate (preview + generate + result groups; CSV export stays in Phase 8), manual assign, adjust, recalculate; `LeaveBalancesController` (`/api/v1/leave/balances`) — My Balances (`leave:read-own`), Team Balances (`leave:read-team`), All Balances (`leave:read`/`leave:manage`). Implements the proration table from spec §4 (calendar-day / working-day / monthly accrual / carry-forward cap / forfeiture) as a pure calculation helper — no EF, no HTTP — per the architecture skill's "Helpers must be pure logic only" rule, so it's unit-testable against the spec's worked example (Priya, §7) directly.
- Frontend: `leave-entitlement.model.ts`, `leave-balance.model.ts`, stores + `entitlements-management` (HR) + `my-balances` (employee) + `team-balances` (manager) feature pages, wired to `/time-off` (My Balances is the default `time-off` landing page), `/time-off/team`, `/time-off/entitlements`.
- **Depends on:** Phase 2 (policy amount is the generation source).
- **Blocks:** Phase 4 (request form needs "N days remaining" per type).

## Phase 4 — Leave Request Submission (Screen 5)

**Status:** written in full — **executed 2026-08-22** on `feat/leave-management-part-4` (unit + architecture verified; live Docker smoke pending). Own submit + preview + HR on-behalf + own list. Pending reserves `PaidDays` only. Cross-year blocked. Half-day `am`/`pm` same-day only. No appsettings escalation owner. Calendar writer not called.

- Backend: `LeaveRequestsController` (`/api/v1/leave/requests`) — Create (half-day, notice-period check, blackout check, overlap check, insufficient-balance + paid/unpaid split, document-required-after-N-days, conflict snapshot from existing Calendar module), List own. Approver resolution: reporting line → team lead → department owner → named person → permission → escalation owner (spec §5) — a dedicated `ILeaveApproverResolver` service, since Phase 5 needs the same resolution logic.
- Frontend: `leave-request.model.ts`, `leave-request-api.service.ts` (extends `leave.store.ts`), `new-request` feature (5-step form matching Screen 5), wired as a modal/route from `/time-off`.
- **Depends on:** Phase 3 (balance preview), existing Calendar module (`calendar:read`, conflict detection).
- **Blocks:** Phase 5, 6, 7.

## Phase 5 — Approval workflow (Screens 6 & 8)

**Status:** written in full — **executed 2026-08-22** on `feat/leave-management-part-5` (unit + architecture verified; live Docker smoke pending). Approval is transactional and outbox-driven: paid days move pending→used on approve, pending is released on reject, request-info pauses/resumes through `leave_request_info_messages`, self-approval is `Leave:Approvals.AllowSelfApproval` (default false), and every new leave side-effect outbox type has a registered no-op handler.

- Backend: `LeaveApprovalsController` (`/api/v1/leave/requests/{id}/approve|reject|request-info`, bulk variants) — approval-mode enforcement (any-one/all/in-order), self-approval block, delegate resolution, current-vs-submission conflict re-check. **Outbox-pattern side effects** (per architecture skill's "Outbox Pattern (required for side effects)" rule): calendar confirm, Workforce Presence "On Leave", payroll deduction flag (unpaid days), notify — saved in the same transaction as the approve, published by the background worker, never called directly from the controller. Spec §5's failure-recovery table (calendar stale / no approver / card not delivered / attendance fails / payroll missing) becomes the integration test matrix for the outbox consumer.
- Frontend: `pending-approvals` feature (`/time-off/team`), `requests-list` (HR, `/time-off/all`), approval detail sheet with conflict/coverage/history panels matching Screen 8.
- **Depends on:** Phase 4.
- **Blocks:** Phase 6.

## Phase 6 — Cancellation (Screen 9)

**Status:** written in full — **executed 2026-08-22** on `feat/leave-management-part-6` (unit + architecture verified; live Docker smoke pending). Cancellation is transactional and outbox-driven: pending/information-requested cancellation releases pending paid-day reservations without a balance audit row, approved cancellation restores used paid days with one `Adjustment` audit row, partial in-progress cancellation restores only stored future paid allocation units, HR cancellation requires a reason, employee reason is config-driven, business date comes from legal-entity timezone plus validated fallback config, and stale writes return the product refresh message through `xmin` concurrency.

- Backend: `POST /api/v1/leave/requests/{id}/cancel` — pending (release pending reservation, no used-balance audit) vs. approved (restore + audit `Adjustment` row + remove calendar/payroll flag through outbox) vs. partial (in-progress leave, only future stored allocations restored, effective-date default from business date). Adds request-day allocation storage so partial cancellation does not depend on recalculating changed working-day/holiday config. HR cancel requires reason; employee cancel reason optional unless configured.
- Frontend: Cancel action on My Leave / All Requests rows, confirm dialog per spec's exact copy.
- **Depends on:** Phase 5.

## Phase 7 — Team Calendar (Screen 7)

**Status:** written in full — **executed 2026-08-23** on the current leave branch (unit + architecture + API build verified; Testcontainers smoke compiles but live run is pending Docker engine). Team Calendar is a read-only, scoped month projection over leave requests plus holiday provider data; `calendar:read` is required but not sufficient on its own, leave visibility still comes from `leave:read-own`, `leave:read-team`, `leave:read`, or `leave:manage`. Tentative blocks and type colors are config-driven through `Leave:Calendar`; no display/business values are hard-coded into handlers.

- Backend: `GET /api/v1/leave/calendar` — month view, who's off + type category/configured color + public holidays, department filter, own/team/all visibility scoping, partial-cancellation-aware projection. Reuses `LeaveRequest`, `LeaveType`, employee scope data, and a holiday provider; no new calendar entity. `DevSmokeTestTenantSeeder` grants `calendar:read` to the smoke HR manager for live route testing.
- Frontend: `team-calendar` feature, wired to `/time-off/calendar`.
- **Depends on:** Phase 4 request data. Phase 5 supplies approved/approval-state transitions; Phase 6 is not a calendar dependency, but the backend plan is partial-cancellation aware when those fields are present.

## Phase 8 — Balance Audit surfacing + bulk generate polish + year-end job

**Status:** written in full — **executed 2026-08-23** on `feat/leave-management-part-8` (isolated worktree, branched from `feat/leave-management-part-7`). All 6 code tasks committed individually; full unit suite green (3105/3105), full architecture suite green (676/676), API + Integration projects build clean. Live Testcontainers/dev-DB run not performed — Docker daemon unreachable in this environment, matching the "pending Docker engine" note already recorded against Phases 3-7.

- Backend: `GET /api/v1/leave/balance-audit` (append-only read, filters) + CSV export, `POST /api/v1/leave/entitlements/generate/preview/export` (CSV export of the existing Phase 3 preview — no new planning logic), and `LeaveYearEndEntitlementJob` (`BackgroundService`, daily-checked, triggers once per Jan 1 UTC per tenant) — the carry-forward/forfeiture math is 100% reused from Phase 3's `LeaveEntitlementCalculator`/`LeaveEntitlementPlanner`; this phase only adds the automatic trigger and the read/export surface. The job does not go through `IMediator`/`GenerateEntitlementsCommand` since that handler depends on `ICurrentUser`, which is unavailable in a background job's DI scope — it calls the planner/repository directly instead, following the same admin-mode + `ITenantContextSwitcher.SwitchToTenantAsync` per-tenant pattern already used by `BulkOnboardingBatchProcessor`.
- Frontend: audit trail panel on Entitlements screen, CSV export buttons (not written yet — backend only in `part-8`).
- **Depends on:** Phase 3 (already shipped — `LeaveEntitlementCalculator`, `LeaveEntitlementPlanner`, and `LeaveBalanceAudit` writes are all live).

## Phase 9 — Hardening

**Status:** written in full — **executed 2026-08-24** on `feat/leave-management-part-9` (branched from `feat/leave-management-part-8`). Architecture test for `LeaveTypesController`, N+1 regression guard for `LeaveBalanceMapping`, and `GetLeaveTypeQueryHandler` tests shipped. Coverlet `XPlat Code Coverage` hung after matching the Leave unit tests (no cobertura file written) so the 70%+ namespace report was not produced; the untested `GetLeaveType` handler was closed instead. Live Priya HTTP e2e against acme did **not** match spec §7 end-to-end in this environment: Docker Desktop engine was down (Testcontainers cannot start), and `dotnet run` of this branch against local `OnevoDb` aborted because hosted jobs query columns the local DB is missing (`bulk_onboarding_batches.resolution_state_json`). Spec §7 calculator numbers are already unit-locked (`Calculate_CalendarProration_MatchesProductWorkedExampleInclusive` ≈ 10.1 for 1 Jul 2026 hire; `Calculate_CarryForward_UsesConfiguredPolicyCap` carry 5 / forfeit 3 from remaining 8). `EXPLAIN ANALYZE` on `leave_entitlements` for the acme tenant used `ix_leave_entitlements_tenant_employee_type_year` (0.169 ms, 0 rows). Repeat the live HTTP run when Docker is up or the local DB is migrated to this branch.

- Architecture test coverage: **closed** — `LeaveTypesControllerArchitectureTests.cs` added (TenantPolicy, `leave:read`/`leave:manage`, IMediator-only, no TenantId on contracts, Code absent from update).
- Coverage check against the 70%+ target: **partial** — collector hung; added `GetLeaveTypeQueryHandlerTests` for the handler that had zero tests.
- Perf on `GET /leave/balances` (All Balances, N+1 risk): **guarded** — `LeaveBalanceMappingPerfTests` asserts one batched policy lookup for 50 rows. Local `EXPLAIN ANALYZE` on `leave_entitlements` is an index scan.
- Frontend: no retirement needed — the 2026-08-17 sketch was design-doc-only and no code from
  it was ever committed (confirmed 2026-08-23: zero Leave-related files in the frontend repo).
  Frontend Phase 1 (`Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-21-leave-management/part-1-leave-types-frontend.md`)
  has not been executed yet — that is a separate, still-pending piece of work, not part of
  this backend hardening phase.
- Full live-dev-DB run of Priya's worked example (spec §7): **blocked in this environment** (Docker down; local `OnevoDb` schema behind this branch). Calculator unit tests already encode the mid-year and year-end numbers.

---

## Open decisions carried into Phase 2+ (flagged now, not resolved here — don't default silently when writing those parts)

- **Blackout period scope** — tenant-wide, per-legal-entity, or per-policy? Spec §2.2 lists it under Policy fields, so default assumption is per-policy (`LeavePolicyBlackoutPeriod` FK's the policy) — confirm against any manager-corrections doc before Phase 2 if one exists (none found for Leave as of this writing, unlike Work Management's "Project Management.md").
- **Max team absence %** — spec doesn't say whether this blocks submission or only warns (Screen 5 lists team-member-count as "warning only, does not block" but Policy's `Max team absence %` field's enforcement isn't explicit). Default: warning only, matching every other Screen 5 conflict, unless Phase 4 brainstorming surfaces a reason to block.
