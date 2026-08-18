# Employee Offboarding Execution — Backend Design

**Status:** Approved by user 2026-08-17, ready for implementation planning.

**Companion spec:** `Hrms--Web-application---front-end---v1/docs/superpowers/specs/2026-08-17-employee-offboarding-execution-frontend-design.md` (frontend consumer of this API — the 6-step offboarding wizard reached from the employee-detail screen's "Offboarding" action). This document is the backend half; the two share the API contract in §6.

**Origin:** brainstormed live with the user 2026-08-17 via `superpowers:brainstorming`, driven by a 6-step offboarding flow image (Start Offboarding → Choose Exit Checklist → Review Tasks → Track Exit Work → Complete or Bypass Tasks → Complete Employee Exit) the user supplied, with the explicit requirement that entry is via an action button on the employee-detail screen, not sidebar-only navigation. Grounded in `docs/superpowers/project_ core/phase1-table-inventory.md`, `ONEVO_Backend_Architecture_Document.md`, and cross-checked against the actual current codebase (see §3 for concrete divergences found).

---

## 1. Goal

Let an HR Admin run a complete employee exit from the employee-detail screen: capture exit details, select and instantiate an offboarding checklist, track and complete (or bypass-with-approval) exit tasks, then close the employee record out as terminated/resigned with access revoked and the record locked read-only.

## 2. Scope

**In scope:** `offboarding_records` (build — documented but never implemented), offboarding-specific extensions to the already-generic `checklist_templates`/`employee_checklist_tasks` (bypass + penalty + category fields), a new `offboarding_task_bypass_requests` table and approval flow, employee-checklist-task CRUD/complete/bypass endpoints (none exist today for either lifecycle type), offboarding completion effects (employment status, user deactivation, session revocation, read-only lock), and a read-only guard on the existing employee-mutation surface.

**Out of scope (Phase boundaries, not deferred-by-oversight):** computed payroll/final-settlement amounts — Payroll is Phase 2, so "final settlement" is a manual checklist task with free-text notes only, never a calculated payout. Real task-reassignment for knowledge handover — Work Management is Phase 2/3; handover is a checklist task, not an integration with `tasks`/`task_approvals`. File-evidence upload on checklist tasks — matches onboarding's current capability (none). A template-authoring UI — the existing `checklist-builder` component is extended, not replaced (see companion frontend spec). External IT system deprovisioning — "access removed" means our own `sessions`/`users.is_active`, not third-party SaaS accounts. An in-app `notifications` row per bypass request — the `notifications`/`notification_channels`/`notification_templates` tables are, like `offboarding_records`, documented in phase1-table-inventory but **never built anywhere in this codebase** (verified: zero `Notification` domain entity, zero migration). Standing up a whole notification subsystem to power one Approval Inbox is out of proportion to this feature; the inbox is a plain query endpoint instead (§6). A new `employee_lifecycle_events` row at completion — that table also doesn't exist yet, is shared history infrastructure for seven other event types (hired/promoted/transferred/...) with no other writer today, and `offboarding_records` already carries reason/dates/status/`completed_at` as the durable record of the exit. Building a shared table for one call site is how the wrong schema gets locked in; add it later against a real reading surface.

## 3. Current-state facts this design depends on

Verified directly against the codebase, not assumed from the inventory docs:

- **`offboarding_records` does not exist anywhere in code** — no entity, no migration, no controller. The phase1-table-inventory documents it (Core HR) but it was never built. This is genuinely new work, not an extension.
- **`Employee.EmploymentStatusId` is an `int` FK into the `employment_statuses` lookup table** (`src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs`), not the `varchar` code the inventory doc describes — same staleness already documented in the 2026-08-15 self-service-profile spec.
- **The `EmploymentStatuses()` seed (`LookupDataSeeder.cs`) currently has only 4 rows**: `1=active, 2=on_leave, 3=suspended, 4=terminated`. Neither `offboarding` nor `resigned` exists yet, despite both being in the inventory doc's code list. This design adds `5=offboarding, 6=resigned` to that seed array (idempotent upsert seeder — no separate data migration needed beyond the app restart that re-runs it).
- **`checklist_templates`/`employee_checklist_tasks` are already generic across onboarding and offboarding** (`lifecycle_type`/`template_type` discriminator), and were already extended by the 2026-08-13 checklist-template-backend-foundation plan with `LegalEntityId`, `PositionId` (template scope) and `IsRequired` (task flag) — confirmed present on the actual entities. Full CRUD exists at `ChecklistTemplatesController` (`/api/v1/people/checklist-templates`), gated `employees:read`/`employees:write`. That same 2026-08-13 plan explicitly scoped offboarding *execution* (bypass, penalties) **out** — so this design is exactly the follow-up it deferred. **However**, `EfChecklistTemplateRepository.InstantiateAsync` currently throws `ArgumentException` unless `template.IsActive && template.TemplateType == "onboarding"` — instantiation is hard-coded onboarding-only today and must be relaxed for offboarding templates to instantiate at all.
- **No controller exists for `employee_checklist_tasks` at all** (searched `*EmployeeChecklistTask*Controller*`, `*ChecklistTask*Controller*` — nothing). Listing an employee's tasks, editing owner/due date, completing a task — none of this is exposed today, for onboarding or offboarding. All of it is new work here (offboarding-scoped; onboarding's equivalent screens are a separate, already-out-of-scope concern).
- **`EmployeesController`'s actual route is `/api/v1/employees`** (verified: `[Route("api/v1/employees")]` in the live controller) — **not** `/api/v1/people/employees` as an earlier draft of this document assumed (`ChecklistTemplatesController` does use a `/people/` prefix, but `EmployeesController` does not; the two controllers are inconsistent with each other in the actual codebase, and this design follows `EmployeesController`'s real prefix since offboarding nests under it). Its existing write surface: `POST {id}/change-position` (`employees:write`), `PUT me/personal-information`, `PUT me/avatar`, `POST/PUT/DELETE me/emergency-contacts`, `POST/PUT/DELETE me/dependents`, `PUT me/payroll`. §7 narrows which of these actually needs an explicit read-only guard.
- **`task_approvals`** (Work Management, Phase 3) is the closest existing approval pattern in this codebase: single named `approver_id`, `status` pending/approved/rejected/cancelled, one pending approval per subject row, `requested_by_id`/`decided_at`/`comment`. The user explicitly pointed to this as the model to follow for bypass requests — **without** touching Work Management tables. `offboarding_task_bypass_requests` (§4.3) mirrors this shape exactly.
- **`Session`** (`src/ONEVO.Domain/Features/Auth/Entities/Session.cs`) has `IsRevoked`, `UserId`, `TenantId`, and an `ISessionRepository` (`RevokeByIdAsync`/`RevokeByKeyHashAsync`, single-session only). **No existing bulk "revoke all sessions for this user" method exists** — `ISessionRepository` needs a new `RevokeAllActiveByUserIdAsync` method; this is new work, not a reuse.
- **`User.IsActive`** exists and is already the established "deny login" mechanism (used at invitation-pending time, per `AcceptEmployeeInvitationCommandHandler`/`AcceptInvitationGoogleCommandHandler`/`AcceptInvitationPasswordCommandHandler`, all of which only ever set it `true`, never `false`). Flipping it to `false` at offboarding completion is new work but reuses the mechanism. **Verified: this isn't merely a login-time check.** `TenantDatabaseTicketStore.RetrieveAsync` (`src/ONEVO.Infrastructure/Identity/Sessions/TenantDatabaseTicketStore.cs:163-227`, the `ITicketStore.RetrieveAsync` implementation ASP.NET Core's cookie auth handler calls on *every* authenticated request to rehydrate the principal) re-checks `!user.IsActive` at line 191 and fails authentication if true — so `User.IsActive = false` transitively blocks every self-service `me/*` endpoint on the very next request, independent of whether session revocation has completed yet. This is why §7's read-only guard doesn't need to touch the `me/*` handlers.
- **`IUserRepository.GetByIdAsync`** (`EfAuthRepository.cs:47-51`) is a tracked query (no `.AsNoTracking()`) — mutating the returned `User.IsActive` then calling `IUnitOfWork.SaveChangesAsync` persists correctly, confirmed by reading the implementation directly rather than assuming.
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

No `notification_id`/notification integration — the `notifications` table doesn't exist in this codebase (§2 non-goals); the approver finds pending requests via the query endpoint in §6, not a push notification.

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
- `offboarding_records.status='completed'`, `completed_at=now`.
- Read-only lock takes effect immediately (§7) — no separate flag needed; the guard reads `EmploymentStatusId`/`offboarding_records.status` directly.

## 6. API surface

All under `/api/v1/employees/{employeeId}` (matching `EmployeesController`'s actual route prefix — see §3), `[Authorize(Policy="TenantPolicy")]`, `tenantId` always server-derived.

| Method | Route | Permission | Purpose |
|---|---|---|---|
| POST | `offboarding` | `employees:offboard` **+ coverage** | Step 1 — start |
| GET | `offboarding` | `employees:read` | Current/latest record + task summary (drives resume + read-only banner) |
| POST | `offboarding/select-checklist` | `employees:offboard` **+ coverage** | Step 2 — instantiate tasks from a template |
| POST | `offboarding/cancel` | `employees:offboard` **+ coverage** | §5.4 |
| GET | `checklist-tasks?lifecycleType=offboarding` | `employees:read` | Steps 3-4 — list/track |
| PATCH | `checklist-tasks/{taskId}` | `employees:write` | Step 3 — owner/due date/required edits |
| POST | `checklist-tasks/{taskId}/complete` | `employees:write` | Step 5 — mark done |
| POST | `checklist-tasks/{taskId}/bypass-requests` | `employees:write` | Step 5 — request bypass |
| POST | `offboarding/complete` | `employees:offboard` **+ coverage** | Step 6 |

"+ coverage" means `employees:offboard` alone is not sufficient — see §11. Task-level actions (patch/complete/bypass) stay at `employees:write`, uncoverage-gated: once an offboarding is properly opened by a covered, permitted actor, ordinary task administration is routine HR write work, not a repeat of the "can this person offboard this employee" decision.

Cross-employee (not nested under one employee):

| Method | Route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/employees/offboarding-overview` | `employees:read` | §11 — the new sidebar screen's list, coverage-scoped |
| GET | `/api/v1/offboarding-bypass-requests?status=pending` | `employees:read` | Approval Inbox — always implicitly scoped to `approverId = current user`; no arbitrary `approverId` override. |
| POST | `/api/v1/offboarding-bypass-requests/{id}/approve` | `employees:write` | Only if `CurrentUser.Id == request.ApproverId` |
| POST | `/api/v1/offboarding-bypass-requests/{id}/reject` | `employees:write` | Same restriction |

## 7. Read-only enforcement (backend-enforced, per user decision)

**Only one call site needs an explicit guard: `ChangeEmployeePositionCommandHandler`** (`src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/`). Reasoning (verified, not assumed — see §3): every `me/*` self-service write is only reachable by the offboarded employee authenticating as themselves, and `User.IsActive = false` (set at Step 6) transitively blocks that on the very next request via `TenantDatabaseTicketStore.RetrieveAsync`'s per-request `IsActive` check — independent of, and not racing with, session-revocation timing. `change-position` is different: it's admin-invoked *on* a target employee by someone else, so deactivating the target's own login does nothing to stop it. It is also, as of this design, the **only** admin-side (non-`me/*`) mutation on `EmployeesController` that touches an arbitrary employee's record — verified by reading the controller in full (§3). The guard: a small check (e.g. `IEmployeeOffboardingLockGuard.EnsureMutable(tenantId, employeeId)`) called at the top of the handler, rejecting with `409 Conflict` once `Employee.EmploymentStatusId` ∈ `{resigned, terminated}`. If a future admin-side employee-mutation endpoint is added elsewhere, it should call the same guard — this isn't a closed list, it's what's true of the codebase today.

## 8. Edge cases

- Bypassing a task the requester is themselves assigned to is allowed (Phase 1 has no self-assignment restriction on tasks generally), but self-*approval* is blocked by the `approver_id != requested_by_id` rule.
- A task's bypass eligibility (`is_bypassable`) is fixed at template-authoring time and copied at instantiation — an HR admin cannot make a non-bypassable task bypassable mid-flow without editing the template (out of scope for the execution flow).
- If every task in a chosen template happens to be non-required, Step 6 is reachable immediately after Step 2 — this is intentional (e.g., a High-Risk Knowledge Exit template with only advisory tasks), not a bug to guard against.
- Cancel after any task bypass approvals have already happened: approved bypass requests are left as-is (historical, no hard delete); only the offboarding record and employment status revert.

## 9. Testing strategy

Unit: `ChecklistTaskJsonContract` extended-field parsing (new fields optional/default), the offboarding-instantiation relaxation (with a regression test proving the existing onboarding path is unchanged), the completion-gate check (required-vs-bypassed-vs-pending, as an independently-tested pure function, not only inside the full transaction), the read-only guard, the approver != requester rule, the one-pending-bypass-per-task constraint. Integration (Testcontainers.PostgreSQL): full Step 1→6 happy path, cancel-then-restart, bypass reject-then-retry, read-only guard returning 409 post-completion on `change-position` only, RLS coverage for the two new tenant-owned tables (`TenantIsolationArchitectureTests` picks these up automatically once the migration declares them in its `TenantTables` array and emits `CREATE POLICY tenant_isolation` — see the implementation plan for the exact mechanism).

## 10. Resolved during implementation-plan research (kept here for traceability)

- `IEmployeeChecklistTaskRepository`/`IChecklistTemplateRepository`'s exact current shape, `ListOnboardingMatchesAsync`'s signature, the `TenantTables` RLS-registration mechanism, and the six candidate read-only-guard handlers were all re-verified directly against the live code (not the 2026-08-13 plan document) before the implementation plan was written — see `docs/superpowers/plans/2026-08-17-employee-offboarding-execution-backend.md` for the resulting exact file paths and signatures.

## 11. Coverage-scoped access and sidebar screen (added 2026-08-18, post-plan user request)

The original design only wired entry via the employee-detail action button and gated every offboarding write on plain `employees:write`. The user asked for a second entry point — a dedicated People-sidebar screen showing employees the caller is management-coverage owner of — with a distinct permission for *starting* offboarding, and strict enforcement that a caller can only offboard employees within their own coverage.

**Existing mechanism reused, not reinvented.** This codebase already has exactly this concept: `IEmployeeVisibilityScopeResolver.ResolveAsync(tenantId, userId, ct) -> EmployeeVisibilityScope` (`src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EmployeeVisibilityScopeResolver.cs`) resolves a caller's `CoveredPositionIds`/`CoveredDepartmentIds`/`CompanyWideLegalEntityIds` from `management_coverage_records`, keyed off the caller's own active-primary position. `EfEmployeeRepository.ListVisibleAsync`/`GetVisibleByIdAsync` (lines ~70-82) already filter employees by exactly this: `employee's active-primary position ∈ CoveredPositionIds OR employee.DepartmentId ∈ CoveredDepartmentIds OR employee.LegalEntityId ∈ CompanyWideLegalEntityIds` (plus a self-match this feature doesn't need). **Per explicit user decision, this feature never substitutes `EmployeeVisibilityScope.Unrestricted()`** — every caller, including org:manage-style admins, is scoped by their literal coverage rows here, stricter than the existing Employees list screen's behavior (which does apply an unrestricted bypass elsewhere in the app — that bypass is simply never reached from this feature's code paths).

**New permission: `employees:offboard`.** Seeded alongside the existing `employees:*` permissions in `PermissionSeeder.cs`, same `core_hr` module ownership. Gates the four offboarding-**record**-lifecycle mutations (Start/SelectChecklist/Cancel/Complete — see §6's revised table); task-level actions remain `employees:write`.

**New reusable guard: `IEmployeeOffboardingCoverageGuard.EnsureCovered(Guid tenantId, Guid actingUserId, Guid targetEmployeeId, CancellationToken ct) -> Task<Result?>`** (`null` = covered, otherwise a `403 Forbidden` `Result` to return immediately) — mirrors §7's `IEmployeeOffboardingLockGuard` shape exactly. Implementation: resolve the caller's `EmployeeVisibilityScope` via the existing resolver, fetch the target employee's active-primary position (`IPositionAssignmentRepository.GetActivePrimaryAsync`, already used elsewhere in this plan), and check the same three-way membership test `ListVisibleAsync` uses (minus the self-match, which is irrelevant here — self-offboarding is already forbidden). Called from all four record-lifecycle handlers: `StartOffboardingCommandHandler`, `SelectOffboardingChecklistCommandHandler`, `CancelOffboardingCommandHandler`, `CompleteOffboardingCommandHandler` — right after the `employees:offboard` permission check has already passed at the controller layer, since permission answers "can this person offboard *anyone*" and coverage answers "can this person offboard *this* employee," two independent questions.

**New endpoint: `GET /api/v1/employees/offboarding-overview`** (`employees:read`, no coverage guard needed on the read itself — the query is inherently coverage-scoped by construction, same principle as `ListVisibleAsync`). Returns, for every employee within the caller's coverage: `employeeId`, `employeeName`, `departmentName`, `positionName`, `currentOffboardingStatus` (nullable — `initiated`/`in_progress`/`completed`/`cancelled`/absent), `canStartOffboarding` (`true` when no open — `initiated`/`in_progress` — record exists). Backs the new sidebar screen (frontend spec §9). Implementation: call the existing `IChecklistTemplateRepository`-sibling pattern — reuse `IEmployeeRepository.ListVisibleAsync(tenantId, scope, filter, page, pageSize, ct)` with the resolver's raw (never-`Unrestricted()`) scope, then batch-fetch each returned employee's latest `OffboardingRecord` via a new `IOffboardingRecordRepository.GetLatestStatusesByEmployeeIdsAsync(tenantId, employeeIds, ct) -> IReadOnlyDictionary<Guid, string>` (one query, not N+1).

**Sidebar entry:** a new "Offboarding" child under the existing "People" nav section (`nav-items.config.ts`), alongside the existing "Employees"/"Checklists"/"Approvals" children, gated `requiredPermissions: ['employees:read']` (viewing the coverage-scoped list needs only read access; the Start action inside the screen is separately gated on `employees:offboard`, mirroring how "Checklists" is nav-gated on `employees:read` while its own write actions require `employees:write`).
