# Part 4: Delete Task (soft delete)

**Read first:** `docs/superpowers/specs/next/2026-08-20-work-management-tree-sprint-task-unified-view-design.md`
§4 — Task rows in the new tree UI need a Delete icon; no delete-task capability exists anywhere today.

**Scope guard:** Work Management module only.

**Status:** shipped 2026-08-21 (Part 4 Tasks 1-5). `DELETE /work/tasks/{id}` soft-deletes via existing `BaseEntity` + `SoftDeleteInterceptor`; objective-owner-only, no permission-bypass.

## Goal

Add the ability to delete a Task from the tree UI. Confirmed with the user: this should be a **soft**
delete (recoverable at the DB level, hidden from all normal reads), matching how the rest of Work
Management treats destructive actions.

## Current state (verified by reading the actual entity/repo/controller, not assumed)

The good news: **no migration is needed.** `WorkTask : BaseEntity`, and `BaseEntity` already declares
`IsDeleted` / `DeletedAt` — these columns already exist on the `tasks` table (created with it, in
`20260816182551_AddTaskFoundationTables.cs`). `ApplicationDbContext`'s global query filter already excludes
`IsDeleted == true` rows from every normal query against `WorkTask`, and `SoftDeleteInterceptor` already
converts any EF `Remove()` on a `BaseEntity` into `IsDeleted = true` + `DeletedAt = now` automatically on
`SaveChanges`. This is the exact mechanism already live and in use for `TaskStatus` (see
`EfTaskStatusRepository.Remove`) and `TaskAssignment` (`EfTaskAssignmentRepository.Remove`) — Task itself
just never had a `Remove` method wired up.

So this is: add `Remove` to the Task repository, add a command+handler that calls it, add a controller
route. No entity change, no migration, no query-filter change.

**Authorization convention for all other Task-module mutations** (`CreateTask`, `DeleteTaskStatus`,
`UnassignTask`, etc.) is uniformly: caller must be the owning Objective's `OwnerId` ("milestone owner") —
not the project lead, not the task's own creator/assignee. Follow the identical pattern:
`if (objective.OwnerId != callerEmployeeId.Value) return Result.Forbidden("Only this milestone's owner can
delete tasks.");` (see `DeleteTaskStatusCommandHandler.cs` for the exact shape to copy).

**Dependents to consider**: `TaskAssignment.TaskId` has a physical FK with `ON DELETE CASCADE` in the DB —
irrelevant here since we're doing an EF-level `Remove()` → `UPDATE ... SET is_deleted = true`, not an actual
`DELETE FROM tasks`, so the cascade never fires and assignment rows are simply left pointing at a
now-soft-deleted task (harmless — they're not independently queried without joining back to a task that
will itself disappear from filtered results). `TaskEditRequest.TaskId` and `TaskCreationRequest.CreatedTaskId`
similarly just become dangling references to a soft-deleted row — acceptable, no cleanup needed (matches how
`Objective`/`Project` soft-delete already leaves their own child records in place).

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs`
- `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`

## Files to create

- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTask/DeleteTaskCommand.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTask/DeleteTaskCommandHandler.cs`
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskCommandHandlerTests.cs`
- `docs/postman-request/Work Management/Delete Task.md`

## Tasks (small, do in order, one commit per task)

1. **Repository**: add `void Remove(WorkTask task);` to `IWorkTaskRepository`, implement in
   `EfWorkTaskRepository` as `_db.WorkTasks.Remove(task);` (identical one-liner to
   `EfTaskStatusRepository.Remove`/`EfTaskAssignmentRepository.Remove` — do not add any manual
   `IsDeleted = true` assignment here, the interceptor does that on `SaveChanges`).

2. **`DeleteTaskCommand`**: `public sealed record DeleteTaskCommand(Guid TaskId) : IRequest<Result>;`
   (returns bare `Result`, not `Result<T>` — matches `DeleteTaskStatus`/`UnassignTask` convention, nothing
   to hand back to the caller).

3. **`DeleteTaskCommandHandler`**:
   - Authenticate, resolve `callerEmployeeId`.
   - Load the task via `GetByIdForTenantAsync` — `NotFound` if missing (already excludes soft-deleted rows
     via the global filter, so a double-delete naturally 404s rather than needing an explicit
     already-deleted check).
   - Load its Objective via `IObjectiveRepository.GetByIdForTenantAsync` — `NotFound` if somehow missing
     (defensive, FK-guaranteed).
   - Authorization: `objective.OwnerId != callerEmployeeId.Value` → `Forbidden("Only this milestone's owner
     can delete tasks.")` (see "Current state" above — do not invent a different rule, e.g. do NOT allow the
     task's assignee to self-delete; that's not how any other Task-module mutation in this codebase is
     gated).
   - `_tasks.Remove(task); await _unitOfWork.SaveChangesAsync(ct); return Result.Success();`
   - Tests: (a) objective owner deletes their own task → success, task then absent from
     `GetByObjectiveIdAsync`/`GetBySprintIdAsync` results (prove the query filter actually excludes it,
     don't just assert `IsDeleted == true` on the tracked entity); (b) non-owner (including a caller who is
     merely a project-level `projects:read`/`*` permission holder but not the objective owner) → `Forbidden`
     — confirm this deliberately does NOT use the tenant-permission-bypass pattern from the Sprint/Task
     *read* handlers (Part 1); delete is owner-only, no permission-bypass path exists for Task mutations
     anywhere else in this module, don't introduce one here; (c) nonexistent `TaskId` → `NotFound`; (d)
     deleting twice → second call `NotFound` (proves the query filter, not a manual flag check, is doing the
     work).

4. **Controller action**: `[HttpDelete("tasks/{id:guid}")] [RequirePermission("projects:access")]` on
   `TasksController`, calling `new DeleteTaskCommand(id)`. Place it near the existing `PATCH
   ("tasks/{id:guid}")` action for route-grouping consistency with how the file is already organized.

5. **Postman doc**: `docs/postman-request/Work Management/Delete Task.md`, full 6-section format
   (method+route, auth/permission/idempotency — note idempotency is "no, second call 404s" per task 3d,
   description, request example — no body, response example — 204/200 empty, error-status table with 403
   and 404, Source section linking this plan file and the handler).

## Definition of done

- All 5 tasks committed individually.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green.
- Full solution `dotnet build` compiles clean.
- `docs/postman-request/Work Management/Delete Task.md` created.
- Confirm via a quick manual grep (`grep -rn "IsDeleted" src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`)
  that the global filter you're relying on is still exactly what this plan describes before assuming it —
  the plan's "no migration needed" claim depends on that filter and the interceptor both still being wired
  up as read during planning; if either has changed, stop and re-plan rather than pushing through.
