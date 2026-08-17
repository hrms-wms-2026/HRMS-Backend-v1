# Employee Offboarding Execution — Backend Design

**Status:** Approved by user 2026-08-17, ready for implementation planning.

**Companion spec:** `Hrms--Web-application---front-end---v1/docs/superpowers/specs/2026-08-17-employee-offboarding-execution-frontend-design.md` (frontend consumer of this API — the 6-step offboarding wizard reached from the employee-detail screen's "Offboarding" action). This document is the backend half; the two share the API contract in §6.

**Origin:** brainstormed live with the user 2026-08-17 via `superpowers:brainstorming`, driven by a 6-step offboarding flow image (Start Offboarding → Choose Exit Checklist → Review Tasks → Track Exit Work → Complete or Bypass Tasks → Complete Employee Exit) the user supplied, with the explicit requirement that entry is via an action button on the employee-detail screen, not sidebar-only navigation. Grounded in `docs/superpowers/project_ core/phase1-table-inventory.md`, `ONEVO_Backend_Architecture_Document.md`, and cross-checked against the actual current codebase (see §3 for concrete divergences found).

---

## 1. Goal

Let an HR Admin run a complete employee exit from the employee-detail screen: capture exit details, select and instantiate an offboarding checklist, track and complete (or bypass-with-approval) exit tasks, then close the employee record out as terminated/resigned with access revoked and the record locked read-only.

## 2. Scope

**In scope:** `offboarding_records` (build — documented but never implemented), offboarding-specific extensions to the already-generic `checklist_templates`/`employee_checklist_tasks` (bypass + penalty + category fields), a new `offboarding_task_bypass_requests` table and approval flow, employee-checklist-task CRUD/complete/bypass endpoints (none exist today for either lifecycle type), offboarding completion effects (employment status, user deactivation, session revocation, lifecycle event, read-only lock), and a read-only guard on the existing employee-mutation surface.

**Out of scope (Phase boundaries, not deferred-by-oversight):** computed payroll/final-settlement amounts — Payroll is Phase 2, so "final settlement" is a manual checklist task with free-text notes only, never a calculated payout. Real task-reassignment for knowledge handover — Work Management is Phase 2/3; handover is a checklist task, not an integration with `tasks`/`task_approvals`. File-evidence upload on checklist tasks — matches onboarding's current capability (none). A template-authoring UI — the existing `checklist-builder` component is extended, not replaced (see companion frontend spec). External IT system deprovisioning — "access removed" means our own `sessions`/`users.is_active`, not third-party SaaS accounts.

## 3. Current-state facts this design depends on

Verified directly against the codebase, not assumed from the inventory docs:

- **`offboarding_records` does not exist anywhere in code** — no entity, no migration, no controller. The phase1-table-inventory documents it (Core HR) but it was never built. This is genuinely new work, not an extension.
- **`Employee.EmploymentStatusId` is an `int` FK into the `employment_statuses` lookup table** (`src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs`), not the `varchar` code the inventory doc describes — same staleness already documented in the 2026-08-15 self-service-profile spec.
- **The `EmploymentStatuses()` seed (`LookupDataSeeder.cs`) currently has only 4 rows**: `1=active, 2=on_leave, 3=suspended, 4=terminated`. Neither `offboarding` nor `resigned` exists yet, despite both being in the inventory doc's code list. This design adds `5=offboarding, 6=resigned` to that seed array (idempotent upsert seeder — no separate data migration needed beyond the app restart that re-runs it).
- **`checklist_templates`/`employee_checklist_tasks` are already generic across onboarding and offboarding** (`lifecycle_type`/`template_type` discriminator), and were already extended by the 2026-08-13 checklist-template-backend-foundation plan with `LegalEntityId`, `PositionId` (template scope) and `IsRequired` (task flag) — confirmed present on the actual entities. Full CRUD exists at `ChecklistTemplatesController` (`/api/v1/people/checklist-templates`), gated `employees:read`/`employees:write`. That same 2026-08-13 plan explicitly scoped offboarding *execution* (bypass, penalties) **out** — so this design is exactly the follow-up it deferred.
- **No controller exists for `employee_checklist_tasks` at all** (searched `*EmployeeChecklistTask*Controller*`, `*ChecklistTask*Controller*` — nothing). Listing an employee's tasks, editing owner/due date, completing a task — none of this is exposed today, for onboarding or offboarding. All of it is new work here (offboarding-scoped; onboarding's equivalent screens are a separate, already-out-of-scope concern).
- **`EmployeesController`'s existing write surface** (`/api/v1/people/employees`): `POST {id}/change-position` (`employees:write`), `PUT me/personal-information`, `PUT me/avatar`, `POST/PUT/DELETE me/emergency-contacts`, `POST/PUT/DELETE me/dependents`, `PUT me/payroll` (`employees:write`). This is the exact write surface the read-only-after-offboarding guard (§7) must cover.
- **`task_approvals`** (Work Management, Phase 3) is the closest existing approval pattern in this codebase: single named `approver_id`, `status` pending/approved/rejected/cancelled, one pending approval per subject row, `requested_by_id`/`decided_at`/`comment`. The user explicitly pointed to this as the model to follow for bypass requests — **without** touching Work Management tables. `offboarding_task_bypass_requests` (§4.3) mirrors this shape exactly.
- **`notifications`** (Shared Platform) already supports everything the image's "Approval Inbox" needs: `recipient_user_id`, `action_required`, `related_entity_type`/`related_entity_id` (polymorphic), `resolved_at`/`resolved_by_id`. No new inbox subsystem is needed — a bypass request creates one row here.
- **`Session`** (`src/ONEVO.Domain/Features/Auth/Entities/Session.cs`) has `IsRevoked`, `UserId`, `TenantId` — trivial to bulk-update. **No existing bulk "revoke all sessions for this user" action was found** (only unrelated Monitoring tray-token refresh code matched `IsRevoked`) — this is new work, not a reuse.
- **`User.IsActive`** exists and is already the established "deny login" mechanism (used at invitation-pending time, per `FinalizeOnboardingDraftCommandHandler`'s `IsActive = false` until acceptance). Flipping it back to `false` at offboarding completion reuses this, not a new access-control concept.
- **`ModuleCatalogSeeder.cs` already seeds a `core_hr.offboarding` feature key** (`Included = true`), alongside `core_hr.onboarding`. This design's endpoints should be reachable under that existing feature gate — no new feature-catalog entry needed.
- **Permission catalog** (`PermissionSeeder.cs`) has `employees:read`/`employees:write`/`employees:delete`/`employees:read-team`/`employees:read:sensitive` — no `employees:offboard`-style permission exists, and none is added: every offboarding write reuses `employees:write`, matching the granularity already established for checklist templates and `change-position`.
- **`Employee.TerminationDate`** (`DateOnly?`) already exists on the entity — offboarding completion sets it from `offboarding_records.last_working_date`, no new column needed there.

## 4. Data model

### 4.1 `offboarding_records` (new table — build as documented, plus gaps found against the image)

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | FK tenants |
| `employee_id` | uuid | FK employees |
| `reason` | varchar(30) | `resignation`, `termination`, `retirement`, `contract_end` |
| `last_working_date` | date | |
| `knowledge_risk_level` | varchar(10) | `low`, `medium`, `high`, `critical` |
| `rehire_eligibility` | varchar(20) | **new** — `eligible`, `not_eligible`, `conditional`. Image's Step 1 "Rehire Eligibility" field; not in the inventory doc. |
| `notes` | text | **new** — Step 1's free-text notes (distinct from `exit_interview_notes`, which is populated later/separately). |
| `checklist_template_id` | uuid | **new**, nullable, FK → checklist_templates. Which template drove instantiation — same role as `onboarding_drafts.selected_template_id`. |
| `exit_interview_notes` | text | nullable |
| `penalties_json` | jsonb | Outstanding loans, notice period, asset recovery, bypass penalties — aggregate/manual entry, not computed. |
| `status` | varchar(20) | `initiated` (Step 1 done) → `in_progress` (checklist selected, Steps 2-5) → `completed` (Step 6 done) → `cancelled` (**new** value — see §5.4). |
| `initiated_by_id` | uuid | **new**, FK → users. Who started the offboarding. |
| `previous_employment_status_id` | int | **new**, nullable, FK → employment_statuses. Snapshot of the employee's status before offboarding started, so Cancel (§5.4) can revert precisely instead of assuming `active`. |
| `created_at` | timestamptz | |
| `updated_at` | timestamptz | **new**, nullable — the record mutates across all 6 steps, unlike the inventory doc's single-write assumption. |
| `completed_at` | timestamptz | **new**, nullable |

**Constraint:** partial unique index on `employee_id` `WHERE status IN ('initiated','in_progress')` — at most one open offboarding per employee at a time.

### 4.2 `checklist_templates` task JSON + `employee_checklist_tasks` (extend existing generic entities)

Template task definitions (`ChecklistTaskDefinition` / `tasks_json`, `ChecklistTaskJsonContract` from the 2026-08-13 plan) gain three optional fields, meaningful only when the owning template's `template_type = 'offboarding'` (frontend gates display; backend just accepts them as optional/default-false, no type-branching validation — YAGNI):

- `isBypassable: bool` (default `false`)
- `bypassPenaltyDescription: string?` (free text — no computed amount, ever)
- `category: string?` — `asset_return` / `access_removal` / `document_handover` / `final_settlement` / `knowledge_handover` / `other`. Drives the Step 4 "Track Exit Work" progress-by-category rollup so the frontend never parses task titles to group them.

`EmployeeChecklistTask` entity gains the same three as real columns (copied at instantiation, never mutated on the template — same convention as every other templated field): `is_bypassable boolean not null default false`, `bypass_penalty_description varchar(500) nullable`, `category varchar(40) nullable`.

`employee_checklist_tasks.status` gains a fourth value: `bypassed`, alongside the existing `pending`/`in_progress`/`completed`. A bypass-approved task is `bypassed`, not `completed` — both count as "done" for the Step 6 completion gate, but the distinction matters for audit and for the Progress Overview.

### 4.3 `offboarding_task_bypass_requests` (new table — mirrors `task_approvals`)

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | FK tenants |
| `employee_checklist_task_id` | uuid | FK employee_checklist_tasks |
| `offboarding_record_id` | uuid | FK offboarding_records — denormalized so the Approval Inbox never needs a 3-way join |
| `requested_by_id` | uuid | FK users |
| `approver_id` | uuid | FK users. Ad hoc, chosen by the requester at request time (per user decision — no standing "Bypass Approver" role/config in Phase 1). Must differ from `requested_by_id` (app-layer rule, mirrors `task_approvals`' distinct-actor pattern). |
| `bypass_reason` | varchar(500) | |
| `penalty_description` | varchar(500) | nullable — prefilled from the task's `bypass_penalty_description`, editable by the requester; "None" is a valid value, matching the image. |
| `status` | varchar(20) | `pending` / `approved` / `rejected` / `cancelled` |
| `requested_at` | timestamptz | |
| `decided_at` | timestamptz | nullable |
| `decision_comment` | varchar(500) | nullable |
| `notification_id` | uuid | nullable, FK → notifications. The notification created for the approver at request time; resolved (`resolved_at`/`resolved_by_id`) when the request is decided. |

**Constraint:** partial unique index on `employee_checklist_task_id` `WHERE status = 'pending'` — one pending bypass request per task, exactly like `task_approvals`.

**Decision rule:** only the request's `approver_id` may approve/reject it (not any `employees:write` holder) — otherwise "pick an approver" is meaningless. Approval sets the task to `bypassed`; rejection/cancellation returns the task to its prior status (`pending`/`in_progress`) unblocked to retry or complete normally.

**Scoping rule:** bypass-request creation is validated against `employee_checklist_tasks.is_bypassable = true` — not against `lifecycle_type`. Since onboarding-template tasks always default `is_bypassable = false` (the checklist builder only exposes the checkbox for offboarding templates — frontend spec §4), this naturally confines the feature to offboarding without a separate lifecycle check.

**Completion race:** while a task has a `pending` bypass request, its `complete` endpoint is blocked (409) — the task must wait for the approve/reject decision before it can also be completed the normal way, avoiding an approval landing on a task that's already `completed`.

## 5. Domain rules / state machine

### 5.1 Step 1 — Start Offboarding
Creates `offboarding_records` (`status='initiated'`, `previous_employment_status_id` = employee's current status), sets `Employee.EmploymentStatusId` → `offboarding` (new lookup id 5). Guarded by the §4.1 unique constraint (can't double-start).

### 5.2 Step 2 — Choose Exit Checklist
Lists active `templateType='offboarding'` templates matching the employee's legal entity/department/position, reusing the matching logic already built for onboarding (`ListOnboardingMatchesAsync` generalized to accept `templateType`, or a parallel `ListOffboardingMatchesAsync` — same repository, same match-level ordering: position → department → company). Instantiates `employee_checklist_tasks` (`lifecycle_type='offboarding'`) via the same `ChecklistTaskJsonContract.ToEmployeeChecklistTasks` used by onboarding finalization, anchored on `last_working_date` (offset-days tasks count down to exit, not up from a start date). Sets `offboarding_records.checklist_template_id`, `status='in_progress'`.

### 5.3 Steps 3-5 — Review / Track / Complete-or-Bypass
Plain CRUD + the bypass flow in §4.3 over `employee_checklist_tasks`. No new state on `offboarding_records` itself.

### 5.4 Cancel (new — not in the image, added because rescinded resignations are a realistic HR scenario)
`POST .../offboarding/cancel`, `employees:write`, only while `status` ∈ `{initiated, in_progress}`. Reverts `Employee.EmploymentStatusId` to `previous_employment_status_id`, sets `offboarding_records.status='cancelled'`. Instantiated `employee_checklist_tasks` are left as historical rows (not deleted — matches this codebase's no-hard-delete convention), simply orphaned from any further action once the record is cancelled.

### 5.5 Step 6 — Complete Employee Exit
Gated: every `employee_checklist_tasks` row for this offboarding must be `completed` or `bypassed` (not `pending`/`in_progress`) if `is_required = true`; non-required tasks don't block. On success, in one transaction:
- `Employee.EmploymentStatusId` → `resigned` if `offboarding_records.reason = 'resignation'`, else → `terminated` (termination/retirement/contract_end all map to `terminated` — the image's Step 6 bullet says only "resigned or terminated", a two-way split, not four).
- `Employee.TerminationDate = offboarding_records.last_working_date`.
- `User.IsActive = false` (reuses the existing invite-pending deny-login mechanism).
- Bulk `Session.IsRevoked = true` for every non-revoked session of that `UserId` (new capability — no existing bulk-revoke action to reuse).
- One `employee_lifecycle_events` row (`event_type` = `resigned`/`terminated` to match, `details_json` carries the offboarding_record_id).
- `offboarding_records.status='completed'`, `completed_at=now`.
- Read-only lock takes effect immediately (§7) — no separate flag needed; the guard reads `EmploymentStatusId`/`offboarding_records.status` directly.

## 6. API surface

All under `/api/v1/people/employees/{employeeId}`, `[Authorize(Policy="TenantPolicy")]`, `tenantId` always server-derived.

| Method | Route | Permission | Purpose |
|---|---|---|---|
| POST | `offboarding` | `employees:write` | Step 1 — start |
| GET | `offboarding` | `employees:read` | Current/latest record + task summary (drives resume + read-only banner) |
| POST | `offboarding/select-checklist` | `employees:write` | Step 2 — instantiate tasks from a template |
| POST | `offboarding/cancel` | `employees:write` | §5.4 |
| GET | `checklist-tasks?lifecycleType=offboarding` | `employees:read` | Steps 3-4 — list/track |
| PATCH | `checklist-tasks/{taskId}` | `employees:write` | Step 3 — owner/due date/required edits |
| POST | `checklist-tasks/{taskId}/complete` | `employees:write` | Step 5 — mark done |
| POST | `checklist-tasks/{taskId}/bypass-requests` | `employees:write` | Step 5 — request bypass |
| POST | `offboarding/complete` | `employees:write` | Step 6 |

Cross-employee (not nested under one employee):

| Method | Route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/people/bypass-requests?status=pending` | `employees:read` | Approval Inbox — always implicitly scoped to `approverId = current user`; no arbitrary `approverId` override. |
| POST | `/api/v1/people/bypass-requests/{id}/approve` | `employees:write` | Only if `CurrentUser.Id == request.ApproverId` |
| POST | `/api/v1/people/bypass-requests/{id}/reject` | `employees:write` | Same restriction |

## 7. Read-only enforcement (backend-enforced, per user decision)

A single shared guard (e.g. `IEmployeeOffboardingLockGuard.EnsureMutable(tenantId, employeeId)`, called at the top of each affected handler — not a MediatR pipeline behavior, since only a subset of employee-write handlers are affected and an explicit call is easier to audit) rejects with `409 Conflict` once `Employee.EmploymentStatusId` ∈ `{resigned, terminated}`. Called from every handler behind §3's enumerated write surface: `change-position`, `me/personal-information`, `me/avatar`, `me/emergency-contacts` (POST/PUT/DELETE), `me/dependents` (POST/PUT/DELETE), `me/payroll`. The exact handler class names are enumerated precisely during implementation planning (they weren't all read in this session), but the route list above is complete and verified.

## 8. Edge cases

- Bypassing a task the requester is themselves assigned to is allowed (Phase 1 has no self-assignment restriction on tasks generally), but self-*approval* is blocked by the `approver_id != requested_by_id` rule.
- A task's bypass eligibility (`is_bypassable`) is fixed at template-authoring time and copied at instantiation — an HR admin cannot make a non-bypassable task bypassable mid-flow without editing the template (out of scope for the execution flow).
- If every task in a chosen template happens to be non-required, Step 6 is reachable immediately after Step 2 — this is intentional (e.g., a High-Risk Knowledge Exit template with only advisory tasks), not a bug to guard against.
- Cancel after any task bypass approvals have already happened: approved bypass requests and their `notifications` rows are left as-is (historical, no hard delete); only the offboarding record and employment status revert.

## 9. Testing strategy

Unit: `ChecklistTaskJsonContract` extended-field parsing (new fields optional/default), the completion-gate check (required-vs-bypassed-vs-pending), the read-only guard, the approver != requester rule, the one-pending-bypass-per-task constraint. Integration (Testcontainers.PostgreSQL): full Step 1→6 happy path, cancel-then-restart, bypass reject-then-retry, read-only guard returning 409 post-completion, RLS coverage for the two new tenant-owned tables (`TenantIsolationArchitectureTests` picks these up automatically once they implement `ITenantOwnedEntity` and get a migration-level policy — no test file edits needed).

## 10. Open items for the implementation plan to resolve

- Exact current shape of `IEmployeeChecklistTaskRepository`/`IChecklistTemplateRepository` (the 2026-08-13 plan's *intended* shape was read from the plan document, not re-verified method-by-method against the live interface file — implementation planning should re-read the actual current interface before extending it).
- Whether `ListOnboardingMatchesAsync` is generalized in place (rename + `templateType` param) or a sibling `ListOffboardingMatchesAsync` is added — either is fine; pick based on how much onboarding call-site churn the rename would cause.
- Precise handler class names for the six read-only-guard call sites in §7.
