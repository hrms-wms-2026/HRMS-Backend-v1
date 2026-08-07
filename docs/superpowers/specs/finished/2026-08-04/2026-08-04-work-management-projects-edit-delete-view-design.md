
# Work Management — Edit/Delete/View Project Endpoints — Design

**Status:** Approved by user 2026-08-04, ready for implementation planning.

**Builds on:** `docs/superpowers/plans/2026-08-03-work-management-foundation.md` (Slice 1 — Create Project only). This design covers the next slice of `ProjectsController`: Edit, Delete, and the two read paths (single + list) that Slice 1 explicitly deferred (`GetById` currently returns `501`).

**Out of scope (deliberately deferred, see `docs/superpowers/next-plan/Project Management.md`):** the Milestone(Objective)-in-charge role/permission/reporting-hierarchy system. That system doesn't exist in the schema yet (`project_members` explicitly forbids a `role` column) and depends on Objective/Task CRUD, neither of which exists yet either. `GetById` returns only a simple `isLead` viewer flag, not a capability/role breakdown.

---

## 1. Endpoints

| # | Method + Route | Auth | Purpose |
|---|---|---|---|
| 1 | `PUT /api/v1/work/projects/{id}` | `projects:write` | Edit Project |
| 2 | `DELETE /api/v1/work/projects/{id}` | `projects:write` **+** caller must equal `project.leadId` | Soft delete |
| 3 | `GET /api/v1/work/projects/{id}` | `projects:read` **OR** caller has an active `project_members` row for this project | Get Project by id (replaces the `501` placeholder) |
| 4 | `GET /api/v1/work/projects/mine` | Authenticated tenant user, no permission required | Caller's own projects (self) |
| 5 | `GET /api/v1/work/projects?userId={userId}` | `projects:read` | Any given user's projects (admin/company-owner path) |

All five follow the existing `ProjectsController` conventions: `[Authorize(Policy = "TenantPolicy")]` at the controller level, `[RequirePermission(...)]` per action where a single required permission applies, MediatR `IRequestHandler`, `Result`/`Result<T>` returned from handlers, and the controller's `result.IsSuccess ? Ok(...) : Problem(...)` ternary — no `ToActionResult()`.

`{id:guid}` route constraint (already used by the Create action's `CreatedAtAction` target) means `/projects/mine` and `/projects/{id:guid}` do not collide — ASP.NET routing matches the literal `mine` segment over the guid-constrained parameter.

### Endpoint 3 (GetById) authorization detail

Single route, not two. The `[RequirePermission]` attribute is **not** used here (it would hard-block members who lack `projects:read`). Instead the handler:

1. Loads the project (404 if it doesn't exist in the tenant, or `is_active = false` — a soft-deleted project is not viewable).
2. Resolves the caller's effective permissions via `IPermissionResolver.ResolveAsync` and checks for `projects:read` (or `*`).
3. If the permission check fails, falls back to checking `IProjectMemberRepository` for an active `(tenantId, projectId, userId)` membership row.
4. If neither grants access → `403 Forbidden`.
5. Response always includes `isLead: bool` — computed as `project.LeadId == currentUser.UserId`, independent of which access path was used (a lead is always also a member via the creator-membership row from Create, but the flag is computed directly, not derived from membership).

---

## 2. Edit Project

**Editable fields:** `name`, `description`, `categoryId`, `startDate`, `targetDate`, `color`, `actualHours`.

**Immutable:** `identifier` — never changes after creation, regardless of whether any tasks exist yet (Task Management isn't built, so the doc's "immutable after first task" condition can't currently be evaluated; treating it as always-immutable is the safe default). If the request body includes an `identifier` different from the current value, return `400`.

**Cascade to Default Objective:** `phase1-table-inventory.md` documents that the Default Objective mirrors the Project's `title`/`description`/`start_date`/`end_date` and "stays in sync on Project edit." The handler must, in the same `IUnitOfWork.SaveChangesAsync` transaction:
- Load the Project's Default Objective (`objectives` row where `project_id = {id}` and `is_default = true`).
- Update its `Title = name`, `Description = description`, `StartDate = startDate`, `EndDate = targetDate`.

**Validation:** Reuses `CreateProjectCommandValidator`'s rules where applicable — `startDate <= targetDate`, `categoryId` must exist/be active/belong to the tenant (`404` if not), same length limits on `name`/`description`/`color` as Create.

**Concurrency:** No optimistic concurrency token. The Foundation slice's recorded deviation (`.UseXminAsConcurrencyToken()` doesn't exist in the installed Npgsql EF Core provider version) explicitly flagged that "whichever future slice first adds an UPDATE path... must research the correct current API before adding a concurrency token." This is that first UPDATE path. Per user decision (2026-08-04), we do **not** block this slice on that research — Edit performs a plain last-write-wins update. **Known limitation, logged here for whoever picks up concurrency-token research next**: concurrent edits to the same project silently overwrite each other with no conflict detection.

**Response:** Updated Project fields (same shape as endpoint 3's GetById response, for consistency) — `200 OK`.

---

## 3. Delete Project (soft delete)

- Sets `projects.is_active = false` and `updated_at`. No cascade to dependent tables (`objectives.is_active`, `project_members.is_active`, `release_calendar.is_active`, etc. are untouched by this action — they retain their own independent lifecycle, out of scope here).
- **Authorization:** controller-level `[RequirePermission("projects:write")]`, plus a handler-side check that `currentUser.UserId == project.LeadId`. If the caller has the permission but is not the lead → `403 Forbidden` with a message distinguishing "you don't have permission" from "only the project lead can delete this project."
- **Idempotency of state, not the `Idempotency-Key` header kind:** if the project is already `is_active = false`, return `409 Conflict` ("project already deleted") rather than silently succeeding — consistent with this codebase's general preference for explicit conflict signaling over silent no-ops (see Create's `409` rules for identifier/label conflicts).
- **Response:** `204 No Content`.

---

## 4 & 5. List Projects (mine + by-user)

Both reuse the existing `PagedRequest` / `PagedResult<T>` pattern (`ONEVO.Application.Common.Models`), already used by `ListTenantsQueryHandler` — `PageNumber` (default 1), `PageSize` (default 20, capped 100), `SortBy`, `SortDirection`.

**Query shape:** `project_members` joined to `projects`, filtered to `project_members.is_active = true AND projects.is_active = true AND project_members.user_id = {target user}`, tenant-scoped via the existing RLS + EF global filter (no manual `tenant_id` filtering needed in the query itself — same pattern as every other repository in this codebase).

**List item fields:** `id`, `name`, `identifier`, `categoryId`, `leadId`, `startDate`, `targetDate`, `color`, `isActive`, `allocatedHours`, `completedHours`, `isLead`.

**Endpoint 4 (`/projects/mine`):** target user = `ICurrentUser.UserId`. No permission check beyond the standard `TenantPolicy` authentication/tenant-context requirement (same auth floor as every other tenant endpoint) — this is explicitly *not* gated by `projects:read`, since it only ever returns the caller's own data.

**Endpoint 5 (`/projects?userId={userId}`):** target user = query param, requires `projects:read`. If `userId` doesn't resolve to any user in the tenant, or resolves to a user with zero active memberships, return an empty page (`200 OK`, `Items: []`) — list semantics, not a "resource not found" `404` (consistent with how list endpoints generally behave versus single-resource `GET`s).

A `project_members` row can, in principle, produce duplicate `projects` rows in this join if a user is a member of the same project through multiple Objectives (the table's uniqueness is `(tenant_id, project_id, objective_id, user_id)`, not `(tenant_id, project_id, user_id)`). The query must `DISTINCT` on `project_id` (or `GROUP BY`) before paging, so a user with multiple Objective memberships in one project sees that project once, not once per Objective.

---

## 5. Response DTOs (Application-layer, mapped to API-layer ViewModels per the existing `ProjectViewModelMapper` split)

- `ProjectDetailResponse` — used by GetById and Edit's response. Fields: `id, name, identifier, categoryId, description, leadId, startDate, targetDate, color, actualHours, allocatedHours, completedHours, isActive, createdAt, updatedAt, isLead`.
- `ProjectListItemResponse` — used by both List endpoints (fields listed in section 4/5 above).
- Both live under `ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/`, next to the existing `ProjectCreationResponse.cs`, and get corresponding `ProjectDetailViewModel` / `ProjectListItemViewModel` in `ONEVO.Api/Contracts/WorkManagement/Projects/`, mapped via an extension of the existing `ProjectViewModelMapper`.

---

## 6. Error handling summary

| Status | Applies to | Cause |
|---|---|---|
| `400` | Edit | Validation failure (dates, lengths, attempted `identifier` change) |
| `403` | Delete, GetById | Delete: caller lacks `projects:write`, or has it but isn't the lead. GetById: caller has neither `projects:read` nor active membership |
| `404` | Edit, Delete, GetById | Project doesn't exist in tenant, or (GetById) exists but `is_active = false`. Edit: `categoryId` invalid |
| `409` | Delete | Project already soft-deleted |

List endpoints (4, 5) don't have resource-specific error cases beyond standard `400`/`403` for malformed paging params / missing permission.

---

## 7. Testing approach

Follows the existing Foundation slice's pattern (`docs/superpowers/plans/2026-08-03-work-management-foundation.md` Task 8-9): xUnit + Testcontainers integration tests hitting real HTTP through the `onevo_app` restricted PostgreSQL role (not the superuser Testcontainers default), so RLS is actually exercised, plus handler/validator unit tests. Specific cases to cover:

- Edit: happy path updates both `projects` and the Default Objective; identifier-change attempt rejected; cross-tenant project id returns 404 (RLS proof, matching the pattern in `2026-07-27-forgot-password-restricted-role-http-rls-proof.md`).
- Delete: lead succeeds; non-lead with `projects:write` gets 403; already-deleted gets 409; soft-deleted project excluded from GetById/List afterward.
- GetById: member without permission succeeds; permission-holder non-member succeeds; neither → 403; `isLead` correct in both paths.
- List: pagination defaults/cap; multi-objective membership doesn't duplicate a project row; `mine` never requires permission; `?userId=` requires `projects:read` and 403s without it.

---

## 8. Deferred work (tracked separately)

- Milestone-in-charge role/permission/reporting-hierarchy system — `docs/superpowers/next-plan/Project Management.md`.
- xmin optimistic concurrency for `projects`/`objectives`/`versions` updates — flagged in `2026-08-03-work-management-foundation.md`'s Task 2 deviation note, still unresolved after this slice (Edit ships without it, per section 2 above).
- Project lifecycle workflow status, schedule-health, approval pipeline, archive/restore, and milestone/task-weighted progress calculation — manager feedback received 2026-08-04, see section 9 below and `docs/superpowers/next-plan/Project Management.md`.

---

## 9. Manager feedback cross-check (2026-08-04)

The user's manager reviewed the broader Onexo Workspace Project Management user journey (UI screens, not this API design directly) and returned a corrections document. Most of it is out of scope for this slice — it targets the frontend screens (a separate repo) and asks for backend capabilities (approval workflow, workflow-status/schedule-health separation, milestone/task-weighted progress, archive-with-restore) that don't exist in the schema yet. Cross-checked against this spec so nothing gets silently missed or silently over-scoped:

**Already satisfied / moot, no change needed here:**
- *"Remove the unrestricted status dropdown from Edit"* — moot on the backend. This API never had a free-form `status` field to expose; `phase1-table-inventory.md`'s `projects` table explicitly forbids one ("superseded by `is_active`"), and section 2 above only lists `name/description/categoryId/startDate/targetDate/color/actualHours` as editable. There is nothing to remove.
- *"Do not permanently delete projects with linked records"* — partially already true. Section 3's Delete is a soft delete (`is_active = false`); it never cascades to or removes `objectives`/`project_members`/`release_calendar`/etc. Full parity with the manager's request needs more than this slice ships (see below).

**Explicitly out of scope for this slice (deferred, not silently added):**
- Rename Delete → Archive with a restore path, archive reason, dependency blocking, and an impact-summary confirmation (milestone/task/document/time-entry counts) — this slice has no Restore endpoint and no dependency-count queries. Adding them is new scope, not a rename.
- Workflow status (Draft/Pending approval/Approved/In progress/On hold/Pending completion/Completed/Cancelled/Archived) separate from schedule health (Upcoming/On track/At risk/Overdue/Completed) — requires new schema; today's only lifecycle signal is `is_active: bool`.
- Approval pipeline for project creation and for baseline changes to an approved project (date/effort/owner/status changes) — no approval tables or handlers exist for Projects today (contrast `role_assignments`/`legal_entity` change-requests elsewhere in the schema, which do have this pattern and could inform the design).
- Milestone/task-weighted progress calculation — blocked on Objective/Task CRUD not existing yet (same dependency already noted in `next-plan/Project Management.md` for the Milestone-in-charge feature).
- Overdue-as-notification-not-auto-modal, date-extension-request-with-approval — UI behavior plus a new request/approval entity; not addressed by GetById/Edit as designed here.
- All UI-template, navigation, card-content, form-layout, and text corrections — frontend-repo concern entirely; this spec defines API contracts only, no screens.

**Disposition:** do not re-open or re-scope this already-approved slice to chase these. Full detail captured as raw context in `docs/superpowers/next-plan/Project Management.md` (backend-relevant items) and in the frontend repo's `docs/superpowers/next-plan/Project Management.md` (UI/UX-relevant items), both awaiting a dedicated `superpowers:brainstorming` session before design work starts.
