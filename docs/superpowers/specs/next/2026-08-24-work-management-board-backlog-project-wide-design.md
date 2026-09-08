# Work Management — Project-Wide Sprints & Tasks (backend design)

**Status:** Approved 2026-08-24 (brainstormed with user, Board/Backlog scope decision confirmed as
"project-wide with filters").

**Companion:** `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-08-24-work-management-board-backlog-project-wide-design.md`

**Baseline:** backend `d01867cf`, frontend `7631ef9` (both on
`feature/rewrite-project-management-19-08-2026-flow-chitect`).

**Scope:** Work Management module only — `src/ONEVO.Domain/Features/WorkManagement/**`,
`src/ONEVO.Application/Features/WorkManagement/**`,
`src/ONEVO.Api/Controllers/Tenant/WorkManagement/**`, `src/ONEVO.Api/Contracts/WorkManagement/**`,
`tests/ONEVO.Tests.Unit/Features/WorkManagement/**`, `docs/postman-request/Work Management/**`. No other
module, no migration (this spec adds read-only query endpoints against existing tables only).

## Problem

The Board and Backlog tabs are scoped to a single module (`Objective`) today —
`GET /work/objectives/{objectiveId}/sprints` and `GET /work/objectives/{objectiveId}/tasks`. The frontend
redesign moves both tabs to project level: one Board/Backlog per project, showing every module's sprints
and tasks together, filterable by Module/Sprint/Category. There is currently no endpoint that returns
sprints or tasks for an entire project — only per-objective or per-sprint lookups exist
(`ObjectivesController`, `SprintsController`, `TasksController`).

## Decision

Add two new read-only endpoints, following the exact pattern already used for
`GET /work/projects/{projectId}/task-statuses` and `.../task-categories` (tenant-scoped project lookup,
`Result<IReadOnlyList<T>>`, same DTO shape as the existing per-objective response so the frontend mapper
code is reused unchanged):

- `GET /work/projects/{projectId:guid}/sprints` (`SprintsController`)
- `GET /work/projects/{projectId:guid}/tasks` (`TasksController`)

Rejected alternative: frontend-only fan-out (fetch the flat objectives list, then N parallel
per-objective sprint/task calls, merge client-side). Explicitly rejected by the user — this codebase has
already moved the equivalent task-status/task-category endpoints from objective-scoped to project-scoped
for the same reason (avoid N+1, one clean request per resource type).

## Authorization (read this before the endpoints — it shapes both handlers)

Verified by reading the actual handlers, not assumed: `GetObjectiveSprintsQueryHandler` and
`GetObjectiveTasksQueryHandler` do **not** use the simple tenant+`IsActive` check that
`GetProjectTaskStatusesQueryHandler` uses. They resolve the caller's employee id, check for a
`projects:read`/`*` permission bypass, and otherwise walk the objective's ancestor chain checking
`IProjectMemberRepository.HasActiveMembershipForAnyObjectiveAsync`. A project-wide endpoint can't repeat
an ancestor-walk per objective (there are many objectives in a project) — instead:

1. `_currentUser.IsAuthenticated` → `Forbidden`. Resolve `callerEmployeeId` via `ICallerIdentityResolver`
   → `Forbidden` if null (same as both existing handlers).
2. `_projects.GetByIdForTenantAsync(tenantId, projectId, ct)` → `NotFound("Project not found.")` if null
   or `!IsActive`.
3. If the caller has `projects:read` or `*` (via `IPermissionResolver`): no further filtering, return
   every sprint/task in the project.
4. Otherwise: call the **already-existing**
   `IProjectMemberRepository.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, projectId,
   callerEmployeeId, ct)` — this already encodes this project's membership/cascade rules (same method the
   existing per-objective handlers' ancestor-walk is emulating one objective at a time). Filter the
   project's sprints/tasks down to only those whose `ObjectiveId` is in that set.

This reuses an existing, already-tested repository method rather than inventing new authorization logic —
confirm during implementation that `GetActiveObjectiveIdsForEmployeeInProjectAsync`'s result set is
equivalent to "would pass the ancestor-walk check for this objective" (it should be, per its name and its
existing callers), and add a handler test asserting a non-privileged member sees only their accessible
modules' sprints/tasks if that equivalence needs double-checking.

Separately, neither `SprintResponse` nor `WorkTaskResponse` carries an `isOwner`/permission field (neither
does today, per-objective). The frontend derives per-row Create/Edit/Delete/status-change button
visibility by cross-referencing each sprint/task's `objectiveId` against the already-existing
`GET /work/projects/{id}/objectives` response, which already carries `IsOwner` per objective — no new
field needed on either response DTO for that.

## Sprints endpoint

`GetProjectSprintsQuery(Guid ProjectId) : Result<IReadOnlyList<SprintResponse>>`

Handler (`GetProjectSprintsQueryHandler`):
1–4. Authorization steps above, filtering by the resolved accessible-objective-id set (skip filtering
   entirely if the `projects:read`/`*` bypass applies).
5. New repository method `ISprintRepository.GetByProjectAsync(tenantId, projectId, ct)` — joins
   `sprints` to `objectives` on `objective_id` filtered to `objectives.project_id = @projectId`, same
   tenant-RLS behavior as every other WM query (no `IgnoreQueryFilters`). Apply the accessible-objective
   filter (from step 4 above) in the handler, in-memory or as a query predicate — either is fine, follow
   whichever style `GetObjectiveTasksQueryHandler`'s existing filtering favors.
6. Map to `SprintResponse` — **identical DTO** to what `GetObjectiveSprintsQueryHandler` already returns
   (`Id, ObjectiveId, Name, StartDate, EndDate, Status, CompletedAt, AchievedAt`, confirmed by reading
   `SprintResponse.cs`). No new response type.

## Tasks endpoint

`GetProjectTasksQuery(Guid ProjectId) : Result<IReadOnlyList<WorkTaskResponse>>`

Handler (`GetProjectTasksQueryHandler`, same shape):
1–4. Same authorization steps.
5. New repository method `IWorkTaskRepository.GetByProjectAsync(tenantId, projectId, ct)` — joins
   `work_tasks` to `objectives` on `objective_id` filtered to `objectives.project_id = @projectId`.
   **Includes sprint-less tasks** (`sprint_id IS NULL`) — no filtering by sprint at the query level; the
   frontend's "Active Sprint" default filter must not hide unsorted tasks, so the backend returns
   everything and lets the frontend decide what's visible.
6. Map to `WorkTaskResponse` — identical DTO to the existing per-objective `GetObjectiveTasksQuery`
   response (`categoryId`, `statusId`, `sprintId`, assignees, etc. — no new fields).

## Routes

```
[HttpGet("projects/{projectId:guid}/sprints")]   // SprintsController, alongside existing objectives/{id}/sprints
[HttpGet("projects/{projectId:guid}/tasks")]      // TasksController, alongside existing objectives/{id}/tasks
```

## Testing

- `GetProjectSprintsQueryHandlerTests`: returns sprints from multiple objectives in the project, excludes
  sprints from a different project, `NotFound` for missing/inactive project, `Forbidden` when unauthenticated.
- `GetProjectTasksQueryHandlerTests`: same shape, plus an explicit case asserting sprint-less tasks
  (`sprintId == null`) are included in the result.
- Repository tests for the two new `GetByProjectAsync` methods against the EF in-memory/test provider,
  confirming the objective→project join is correct and tenant-isolated.

## Out of scope

- Any change to the existing per-objective sprint/task endpoints (they stay, Tree tab still uses them).
- Any migration — both new endpoints are pure reads against existing tables/columns.
- Per-row permission fields on the new DTOs — frontend derives ownership from the objectives list, per
  Authorization above.
- Write endpoints (create/edit/delete stay exactly as they are, still keyed by objectiveId/sprintId/taskId).
