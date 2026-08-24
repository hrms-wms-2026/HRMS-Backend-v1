# Leave Management (Backend) — Phase Index

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`
**Companion (frontend):** `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`
**Source of truth for product behaviour:** `C:\HR\leave-management-complete.md`

This is a 10-phase build (0-9), one deliverable slice per phase, backend and frontend paired where a screen exists. Written in full for Phase 0+1 only (`part-1-schema-and-leave-types.md`) — later phases are scoped here (entities/endpoints/files/dependencies/exit-criteria) but not yet broken into bite-sized TDD steps. Write each part's full TDD file when that phase starts, following `part-1`'s pattern.

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

- Backend: `LeavePoliciesController` (`/api/v1/leave/policies`) — List, Get, Create (multi-type + blackout periods + legal-entity assignment + activate/replace-confirmation), Clone. Validate: monthly accrual × 12 ≤ type's annual limit; one active policy per legal entity (replace-confirm flow, spec Screen 2 errors table).
- Frontend: `leave-policy.model.ts`, `leave-policy-api.service.ts`, `leave-policy.store.ts`, `policy-management` feature (multi-step form matching Screen 2's 7 steps), wired to `/time-off/policies`.
- **Depends on:** Phase 1 (leave type picker), `org:read` (legal entity picker, existing).
- **Blocks:** Phase 3.

## Phase 3 — Entitlements + Balance screens (Screens 3 & 4)

- Backend: `LeaveEntitlementsController` (`/api/v1/leave/entitlements`) — bulk generate (preview + generate + results with CSV), manual assign, adjust, recalculate; `LeaveBalancesController` (`/api/v1/leave/balances`) — My Balances (`leave:read-own`), Team Balances (`leave:read-team`), All Balances (`leave:read`/`leave:manage`). Implements the proration table from spec §4 (calendar-day / working-day / monthly accrual / carry-forward cap / forfeiture) as a pure calculation helper — no EF, no HTTP — per the architecture skill's "Helpers must be pure logic only" rule, so it's unit-testable against the spec's worked example (Priya, §7) directly.
- Frontend: `leave-entitlement.model.ts`, `leave-balance.model.ts`, stores + `entitlements-management` (HR) + `my-balances` (employee) + `team-balances` (manager) feature pages, wired to `/time-off` (My Balances is the default `time-off` landing page), `/time-off/team`, `/time-off/entitlements`.
- **Depends on:** Phase 2 (policy amount is the generation source).
- **Blocks:** Phase 4 (request form needs "N days remaining" per type).

## Phase 4 — Leave Request Submission (Screen 5)

- Backend: `LeaveRequestsController` (`/api/v1/leave/requests`) — Create (half-day, notice-period check, blackout check, overlap check, insufficient-balance + paid/unpaid split, document-required-after-N-days, conflict snapshot from existing Calendar module), List own. Approver resolution: reporting line → team lead → department owner → named person → permission → escalation owner (spec §5) — a dedicated `ILeaveApproverResolver` service, since Phase 5 needs the same resolution logic.
- Frontend: `leave-request.model.ts`, `leave-request-api.service.ts` (extends `leave.store.ts`), `new-request` feature (5-step form matching Screen 5), wired as a modal/route from `/time-off`.
- **Depends on:** Phase 3 (balance preview), existing Calendar module (`calendar:read`, conflict detection).
- **Blocks:** Phase 5, 6, 7.

## Phase 5 — Approval workflow (Screens 6 & 8)

- Backend: `LeaveApprovalsController` (`/api/v1/leave/requests/{id}/approve|reject|request-info`, bulk variants) — approval-mode enforcement (any-one/all/in-order), self-approval block, delegate resolution, current-vs-submission conflict re-check. **Outbox-pattern side effects** (per architecture skill's "Outbox Pattern (required for side effects)" rule): calendar confirm, Workforce Presence "On Leave", payroll deduction flag (unpaid days), notify — saved in the same transaction as the approve, published by the background worker, never called directly from the controller. Spec §5's failure-recovery table (calendar stale / no approver / card not delivered / attendance fails / payroll missing) becomes the integration test matrix for the outbox consumer.
- Frontend: `pending-approvals` feature (`/time-off/team`), `requests-list` (HR, `/time-off/all`), approval detail sheet with conflict/coverage/history panels matching Screen 8.
- **Depends on:** Phase 4.
- **Blocks:** Phase 6.

## Phase 6 — Cancellation (Screen 9)

- Backend: `POST /api/v1/leave/requests/{id}/cancel` — pending (no-op balance) vs. approved (restore + audit `Adjustment` row + remove calendar/payroll flag) vs. partial (in-progress leave, only future days restored, effective-date default today). HR cancel requires reason; employee cancel reason optional.
- Frontend: Cancel action on My Leave / All Requests rows, confirm dialog per spec's exact copy.
- **Depends on:** Phase 5.

## Phase 7 — Team Calendar (Screen 7)

- Backend: `GET /api/v1/leave/calendar` — month view, who's off + type colour + public holidays, department filter. Reuses `LeaveRequest` + existing Calendar/holiday data; no new entity.
- Frontend: `team-calendar` feature, wired to `/time-off/calendar`.
- **Depends on:** Phase 4 (approved requests to display).

## Phase 8 — Balance Audit surfacing + bulk generate polish + year-end job

- Backend: `GET /api/v1/leave/balance-audit` (append-only read, filters), year-end carry-forward + forfeiture background job (uses Phase 3's pure calculation helper), CSV export endpoints (entitlement generation results, balance export).
- Frontend: audit trail panel on Entitlements screen, CSV export buttons.
- **Depends on:** Phase 3.

## Phase 9 — Hardening

- Full `ONEVO.Tests.Architecture` pass (dependency direction, tenant isolation, no controller-to-DbContext) for every new `Leave` namespace.
- Coverage check against the 70%+ target (architecture skill NFR).
- Frontend: retire the 2026-08-17 mocked `LeaveApiService`/fixtures entirely — this plan's real `leave-request-api.service.ts` replaces it; delete the old fixture files once `/time-off/*` routes are live.
- Perf pass on `GET /leave/balances` (All Balances, N+1 risk — batched entitlement + used/pending aggregation, matching the `CountActiveEmployeesByDepartmentIdsAsync` batched-query pattern already used in Department).
- Full live-dev-DB run of every phase's golden path in one sitting (Priya's worked example from spec §7, end to end).

---

## Open decisions carried into Phase 2+ (flagged now, not resolved here — don't default silently when writing those parts)

- **Blackout period scope** — tenant-wide, per-legal-entity, or per-policy? Spec §2.2 lists it under Policy fields, so default assumption is per-policy (`LeavePolicyBlackoutPeriod` FK's the policy) — confirm against any manager-corrections doc before Phase 2 if one exists (none found for Leave as of this writing, unlike Work Management's "Project Management.md").
- **Max team absence %** — spec doesn't say whether this blocks submission or only warns (Screen 5 lists team-member-count as "warning only, does not block" but Policy's `Max team absence %` field's enforcement isn't explicit). Default: warning only, matching every other Screen 5 conflict, unless Phase 4 brainstorming surfaces a reason to block.
