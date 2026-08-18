# Work Management — Task Foundation (Task Board, Allocation Cascade, Approvals, Notification) — Backend Design

**Status:** Designed, pending implementation planning.

**Scope guardrail:** Work Management module only — `ONEVO.Domain/Features/WorkManagement/*`, `ONEVO.Application/Features/WorkManagement/*`, `ONEVO.Api/Controllers/Tenant/WorkManagement/*`, related EF migrations/configurations, `docs/postman-request/Work Management/`. The new Notification tables/service are genuinely shared infrastructure (see §6) — placed under `Features/SharedPlatform/Notifications/*`, following the existing `Outbox` module's placement — but built now because Work Management is their first and only consumer; do not touch any other module's existing features while building them. Do not touch Core HR, Org Structure, Calendar (`calendar_events` and its 4 sibling tables — undesigned/unbuilt but owned by a teammate's module, Pillar 1), or any other pillar.

**Origin:** brainstormed live with the user 2026-08-16 via `superpowers:brainstorming`.

**Builds on:** `docs/superpowers/specs/finished/2026-08-04/2026-08-04-work-management-milestone-hierarchy-design.md` (`objective_change_requests`, Reporting Manager routing), `docs/superpowers/specs/next/2026-08-14-work-management-objective-member-management-design.md` (invite/accept pattern), and the Phase 2 Employee-identity migration (commits `de5fdea`..`06d11fc`, `ICallerIdentityResolver`). Table shapes for `task_statuses`/`tasks`/`task_assignments` are taken as-is from `docs/superpowers/project_ core/phase1-table-inventory.md` lines 2752-2848 (Task Management + Worklogs) — this design does not redefine those, only narrows which columns are built now vs. deferred (§1) and adds the new tables/flows the inventory doesn't cover.

---

## 1. Scope for this slice (YAGNI cut from the 15-table Task Management + Worklogs group)

**Building now:**
- `task_statuses` — as documented (project template + per-Objective copy).
- `tasks` — as documented, **except**: omit `sprint_id`, `version_id` (Sprint Planning and Version-linkage are not part of this slice — add as a nullable column in a later migration when Sprints are built, not now).
- `task_assignments` — as documented, **except**: omit the HR-availability-check enrichment (`availability_status`/`availability_checked_at`/`availability_warning` columns and their check logic) — that depends on Time & Attendance, a different pillar. Keep only `id`, `task_id`, `user_id`, `employee_id`, `assigned_by_id`, `assigned_at`.
- `task_creation_requests` — **new table**, §3.
- `objective_change_requests` — **extend** with a new `request_type = 'extend_allocation'` (§4), no schema change beyond that enum value.
- Notification foundation — **new**, §6.
- `GET /api/v1/work/my-deadlines` — **new**, §7.

**Explicitly deferred, not built in this slice:** `time_logs`, `task_checklists`/`task_checklist_items`, `task_comments`, `task_tags`, `task_approvals` (status-transition approval — different concern from `task_creation_requests`, revisit once the Kanban drag interaction is being built for real), `task_progress_updates`, `task_time_correction_requests`, `task_watchers`, `task_links`, `custom_fields`/`custom_field_values`, all of Sprint Planning (5 tables), all of Collaboration (5 tables), GitHub Repository Integration (6 tables).

## 2. Identity: EmployeeId only, no separate LegalEntityId column

Every new FK to a person in this slice (`task_assignments.employee_id`, `task_creation_requests.requested_by_employee_id`/`decided_by_employee_id`, notification recipients) uses **EmployeeId**, consistent with the Phase 2 migration already shipped for `objectives`/`projects`/`project_members`. No table in this slice adds a `legal_entity_id` column — the chain `tasks.objective_id → objectives.project_id → projects.owning_legal_entity_id` already makes Legal Entity derivable wherever needed (e.g., tenant/legal-entity-scoped reporting), so a redundant column would just be denormalization with no read pattern that needs it in this slice. `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync` is reused as-is; it does not need to grow a LegalEntityId return value for this slice.

## 3. Task creation & allocation

### 3.1 Allocation invariant (slack model, not strict equality)

For every Objective: `objectives.allocated_hours >= SUM(child_objectives.allocated_hours) + SUM(direct_tasks.estimated_hours)`. The unused remainder is that Objective's **slack**. This generalizes the same rule the inventory doc already states loosely for Project→Objective and extends it to Objective→Task, and makes it a **blocking** check (not the existing system-wide warning-only convention) for this specific chain, per explicit user decision.

Computed on every task create/edit (`estimated_hours` change) and every Objective create/edit (`allocated_hours` change): `slack(objectiveId) = objectives.allocated_hours - (SUM of active child objectives' allocated_hours) - (SUM of active tasks' estimated_hours in this objective)`.

### 3.2 Task creation — two paths, no approval for either creator role's own budget-respecting create

- **Objective owner** (`objectives.owner_id`) creates a task directly: `POST /api/v1/work/objectives/{objectiveId}/tasks`. Blocked with `409` (`INSUFFICIENT_OBJECTIVE_ALLOCATION`) if `estimated_hours > slack(objectiveId)`. Response on block includes `availableSlackHours` and a `suggestedAction: "extend_allocation"` hint (the frontend uses this to offer the extend-allocation flow, §4) — no separate "preview" endpoint.
- **Objective member** (non-owner, active `project_members` row on this Objective) cannot create a task directly. They submit `POST /api/v1/work/objectives/{objectiveId}/task-creation-requests` (§3.3) with the same proposed task fields. No slack check at submission time — the check happens at approval time, against slack as of that moment.
- The Objective owner's own creation still simply fails with `409` if it doesn't fit — the owner does not go through `task_creation_requests` for their own tasks (no approval step for the owner, matching the existing "creator never needs approval for their own actions" convention). They resolve the block by first running the extend-allocation flow (§4), then retrying creation.

### 3.3 `task_creation_requests` (new table)

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `objective_id` | `uuid` | FK -> objectives |
| `requested_by_employee_id` | `uuid` | FK -> employees |
| `payload_json` | `jsonb` | proposed task fields: `title`, `description`, `taskType`, `priority`, `dueDate`, `estimatedHours`, `storyPoints` |
| `status` | `varchar(20)` | `pending` / `approved` / `rejected` / `cancelled` |
| `decided_by_employee_id` | `uuid` | nullable; FK -> employees; always the Objective owner at decision time |
| `decision_comment` | `text` | nullable; required on rejection |
| `created_task_id` | `uuid` | nullable; FK -> tasks; set on approval |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

**Indexes:** `(tenant_id, objective_id, status)`. **Unique:** none needed — a member may have multiple pending requests on the same objective (different tasks).

**Approve** (`POST /task-creation-requests/{id}/approve`, Objective-owner-only): re-checks slack at decision time (not creation-request time — the objective may have changed since). If it fits, creates the `tasks` row from `payload_json`, sets `created_task_id`, marks `approved`. If it no longer fits, returns `409` with the same `INSUFFICIENT_OBJECTIVE_ALLOCATION` shape as §3.2 — the owner must run the extend-allocation flow themselves before retrying the approval. **Reject** (`POST .../reject`, requires `decision_comment`) — no side effects. **Cancel** (`POST .../cancel`, requester-only, while `pending`).

## 4. Allocation-extend request (`objective_change_requests`, `request_type = 'extend_allocation'`)

Reuses the existing table and its existing routing convention (`reporting_manager_id`, snapshotted at request-creation time; one pending request per Objective already enforced by the existing partial-unique index — this new request type shares that same slot, so an Objective cannot have both a pending `edit`/`transfer`/etc. request and a pending `extend_allocation` request simultaneously). `payload_json: { requestedAdditionalHours: number, reason: string }`.

**Approval is conditional on the approver's own slack**, not unconditional like the other request types:

1. Employee X's Objective needs `+N` hours (triggered by the `409` in §3.2/§3.3, or requested proactively). `POST /objectives/{id}/allocation-requests` creates the row, routed to `reporting_manager_id` (X's Objective's current parent-Objective owner) exactly like every other `objective_change_requests` type.
2. Approver reviews `GET /objectives/allocation-requests/mine`. On `POST .../approve`:
   - If `slack(approver's own objective) >= N` — approve immediately: `objectives.allocated_hours += N` on the **requesting** (child) objective only. The approver's own `allocated_hours` is unchanged (the `N` hours simply come out of the approver's existing unallocated slack). Row → `approved`.
   - If `slack(approver's own objective) < N` — the approve action is rejected with `409` (`APPROVER_INSUFFICIENT_SLACK`). The approver cannot approve yet. They must first submit their **own** `extend_allocation` request (same endpoint, on their own objective, same or larger `N`) to **their** reporting manager. The original child request stays `pending` throughout — untouched, no auto-generated linked row, no cascade-on-submit. Once the approver's own request is later approved (their own `allocated_hours` grows, giving them slack), they return to the still-pending original request and approve it normally (case above).
3. **Root case:** an Objective whose `reporting_manager_id IS NULL` has no `objective_change_requests` routing (unchanged existing rule — Project-level actions never create a row here). For the Default Objective specifically, "extend allocation" is a direct `PATCH /api/v1/work/projects/{id}` edit by the Project creator (`lead_id`) — no request/approval table involved, matching how Project-level edits already work today.

No new schema beyond the `extend_allocation` enum value and its `payload_json` shape — `ObjectiveChangeRequestTypes` (existing static class) gains one member.

## 5. `tasks`/`task_statuses`/`task_assignments` — endpoints

Standard CRUD following the existing Work Management CQRS layout (`Features/WorkManagement/Tasks/{Commands,Queries,DTOs,RepositoryInterfaces}`), MediatR handlers, `IUnitOfWork.ExecuteInTransactionAsync` for mutations:

- `GET /api/v1/work/objectives/{objectiveId}/task-statuses` — the Objective's status columns (auto-copied from the Project template on first access if none exist yet, matching the inventory's documented "each Objective receives an independent copy" behavior).
- `PATCH /api/v1/work/objectives/{objectiveId}/task-statuses/{id}` — rename/reorder/toggle `requires_approval` — **Objective-owner-only** (this is the "can only the Objective owner change task status configuration" rule from the original ask).
- `GET /api/v1/work/objectives/{objectiveId}/tasks` (Board — grouped by `status_id`) and `.../tasks?view=backlog` (flat list) — same endpoint, `view` query param changes shaping only, no separate route.
- `PATCH /api/v1/work/tasks/{id}` — edit fields; `estimated_hours` increase re-runs the §3.1 slack check exactly like create.
- `PATCH /api/v1/work/tasks/{id}/status` — move between columns. Since `task_approvals` (per-status approval bypass) is explicitly deferred (§1), every status move is unconditional in this slice — any Objective member with task access can move any task. Revisit when `task_approvals` is built.
- `POST /api/v1/work/tasks/{id}/assignments`, `DELETE /api/v1/work/tasks/{id}/assignments/{employeeId}` — simple add/remove, no availability enrichment (§1).

## 6. Notification foundation (new, `Features/SharedPlatform/Notifications/*`)

In-app only in this slice; mail channel is schema-ready but not wired (§6.4).

### 6.1 `notification_templates`

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `code` | `varchar(100)` | unique, e.g. `work_task_creation_request_created` |
| `in_app_title_template` | `varchar(255)` | `{{placeholder}}` tokens |
| `in_app_body_template` | `text` | |
| `mail_subject_template` | `varchar(255)` | nullable for now |
| `mail_body_template` | `text` | nullable for now |
| `in_app_enabled` | `boolean` | default true |
| `mail_enabled` | `boolean` | default **false** in this slice |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | nullable |

Not tenant-scoped (global, like `LookupDataSeeder`-style tables) — templates are product copy, not tenant configuration, in this slice.

### 6.2 `notifications`

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `recipient_user_id` | `uuid` | FK -> users — notification fields stay UserId, matching the existing scope boundary already drawn for `ReleaseCalendarEntry.RecipientUserId`/`AuditLog.UserId` during the Phase 2 identity migration (audit/notification fields were deliberately left UserId; only business ownership fields moved to EmployeeId) |
| `template_code` | `varchar(100)` | FK -> notification_templates.code |
| `title` | `varchar(255)` | rendered |
| `body` | `text` | rendered |
| `related_entity_type` | `varchar(40)` | `task` / `task_creation_request` / `objective_change_request` |
| `related_entity_id` | `uuid` | |
| `is_read` | `boolean` | default false |
| `read_at` | `timestamptz` | nullable |
| `created_at` | `timestamptz` | |

**Indexes:** `(tenant_id, recipient_user_id, is_read, created_at)`.

### 6.3 Seeder

`NotificationTemplateSeeder`, same startup-seeding convention as `LookupDataSeeder`. Seeds the four codes needed by this slice: `work_task_creation_request_created`, `work_task_creation_request_decided`, `work_allocation_extend_request_created`, `work_allocation_extend_request_decided`.

### 6.4 Dispatch

`INotificationDispatcher` (`src/ONEVO.Application/Common/ServiceInterfaces/INotificationDispatcher.cs`) already declares `SendToUserAsync`/`SendToTenantAsync`/`SendToGroupAsync` but has **zero implementations today** (confirmed by repo search). This slice adds the first real implementation: `SendToUserAsync(userId, templateCode, placeholderValues, relatedEntity)` looks up the template, renders `{{placeholder}}` tokens against `placeholderValues`, inserts a `notifications` row if `in_app_enabled`. If `mail_enabled` is false (the default in this slice), the mail half is a no-op — no `IOutboxWriter.EnqueueAsync` call yet. Wiring `mail_enabled = true` to an actual `IOutboxWriter` call is deferred to a later slice, reusing the existing Outbox mechanism already used by other modules (per explicit user instruction: extend the existing Outbox, do not build a second async-dispatch system).

Call sites in this slice: `task_creation_requests` create/approve/reject (→ Objective owner / requester), `extend_allocation` create/approve/reject (→ reporting manager / requester).

### 6.5 API

- `GET /api/v1/notifications?unreadOnly=&page=` — caller's own notifications, newest first.
- `GET /api/v1/notifications/unread-count`.
- `POST /api/v1/notifications/{id}/read`, `POST /api/v1/notifications/read-all`.

These are generic (not Work-Management-prefixed routes), living under a new `NotificationsController` — first real consumer is Work Management, but the controller/table/service are placed in Shared Platform per §0 so any future module can call `INotificationDispatcher` without duplicating this table.

## 7. Calendar exposure (read-only, no `calendar_events` writes)

`GET /api/v1/work/my-deadlines?from=&to=` — returns two lists for the caller:
- Objectives where caller is `owner_id`, with `end_date`.
- Tasks where caller has an active `task_assignments` row, with `due_date`.

No new table. This is the entire Work Management side of Calendar integration — the Calendar module (teammate-owned, Pillar 1, not yet built) is expected to call this endpoint (or an equivalent internal query) to project deadline chips at read-time, matching the inventory's existing note that "Task due-date chips ... are projected at read-time, not stored."

## 8. Out of scope / explicitly not this slice

- Mail sending (Outbox wiring) — §6.4.
- `task_approvals` (per-status-column approval bypass) — §1, §5.
- Everything in §1's deferred list.
- Any change to `calendar_events` or its 4 sibling tables.
- Any change to already-shipped Phase 2 EmployeeId columns (`objectives.owner_id`, `projects.lead_id`, etc.) — this slice only adds new columns/tables, never touches those.
