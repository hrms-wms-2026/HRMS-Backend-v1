# Part 1: Collapse Task Status from per-Objective to per-Project

**Read first:** `docs/superpowers/specs/next/2026-08-21-work-management-project-scoped-task-status-and-category-design.md`
§2, and `2026-08-21-work-management-cascading-objective-ownership-design.md` §3 (the
`IsEffectiveManagerAsync` helper this Part's new authorization check depends on — **Part 1 of the
cascading-ownership plan must ship before this Part starts**).

**Scope guard:** Work Management module only.

## Goal

Every Task Status command/query currently keys off an `ObjectiveId` and operates on that Objective's own
copy of the status rows. Re-key everything to `ProjectId`, operating directly on the
`ObjectiveId == null` template rows that already exist for every Project (seeded at Project creation —
no new migration needed, this Part only changes which rows get read/written). Stop creating any more
per-Objective copies.

**Authorization decision for this Part:** Task Status is Project-level configuration, but the user
confirmed non-owner members should be able to change it (not Project-Lead-only, matching
`EditProjectCommandHandler`'s stricter `project.LeadId` pattern). This Part treats the Project's
**default Objective** as the project-level "root" for authorization purposes (consistent with how
`GetObjectiveTreeQueryHandler`'s `hasDirectMembership` branch already treats default-Objective
membership as project-wide access) — the check becomes
`await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct)`.

## Current state (verified by reading every file directly)

- `ITaskStatusRepository` (`src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/`)
  already has `GetProjectTemplateAsync(tenantId, projectId, ct)` — returns `ObjectiveId == null` rows,
  `AsNoTracking`, ordered by `DisplayOrder`. `EfTaskStatusRepository`'s implementation confirms this
  project's convention: fetch `AsNoTracking`, mutate the returned entity in memory, then call the
  repository's explicit `Update(entity)` before `SaveChangesAsync` — EF still detects the change. No new
  repository method is needed for this Part; reuse `GetProjectTemplateAsync` everywhere a handler
  currently calls `GetByObjectiveIdAsync`.
- `GetByIdForTenantAsync(tenantId, id, ct)` on the same repository returns a **tracked** entity (no
  `AsNoTracking`) — this is what `EditTaskStatusCommandHandler`/`DeleteTaskStatusCommandHandler` already
  use for their single-row fetch, and stays that way.
- `IObjectiveRepository.GetDefaultByProjectIdAsync(tenantId, projectId, ct)` already exists — use this to
  resolve the default Objective for the new authorization check (§ above).
- `GetObjectiveTaskStatusesQueryHandler.cs` (full current body already quoted in the design spec §2) —
  today: look up the Objective, return its own copy if any rows exist, otherwise lazily copy the Project
  template onto it and return that.
- `CreateTaskStatusCommandHandler.cs:42-47` — resolves an Objective by `request.ObjectiveId`, checks
  `objective.OwnerId != callerEmployeeId.Value`, creates a row with `ObjectiveId = objective.Id`.
- `EditTaskStatusCommandHandler.cs:38-48` — fetches the status by id, **requires
  `status.ObjectiveId is not null`** (rejects template rows outright today — this is the actual reason a
  Project-level template row can never be edited directly right now), resolves that Objective, checks
  `objective.OwnerId != callerEmployeeId.Value`.
- `DeleteTaskStatusCommandHandler.cs:36-46` — same `status.ObjectiveId is not null` requirement, same
  Objective-owner check, plus a `_tasks.AnyActiveByStatusIdAsync` guard (unrelated, keep as-is).
- `ReorderTaskStatusesCommandHandler.cs:41-59` — resolves an Objective by `request.ObjectiveId`, checks
  `objective.OwnerId != callerEmployeeId.Value`, loads existing rows via `GetByObjectiveIdAsync`.
- `MoveTaskStatusCommandHandler.cs:63-65` — `if (newStatus is null || newStatus.ObjectiveId !=
  task.ObjectiveId) return Result.NotFound("Target status not found.");` — this validates the target
  status belongs to the task's own Objective; must become a Project match instead.

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTaskStatuses/` — rename the
  whole folder/query/handler/response-mapping to `GetProjectTaskStatuses` (`ProjectId` param).
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommand.cs`
  and its handler + validator.
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskStatus/DeleteTaskStatusCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommand.cs`
  and its handler.
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`
- `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (or wherever these routes live —
  `grep -rln "task-statuses" src/ONEVO.Api/`) — route changes from
  `.../objectives/{objectiveId}/task-statuses` to `.../projects/{projectId}/task-statuses`.
- Matching test files — `grep -rln "TaskStatusCommandHandlerTests\|ObjectiveTaskStatusesQueryHandlerTests\|MoveTaskStatusCommandHandlerTests" tests/`.

## Task 1: `GetProjectTaskStatuses` (rename + re-scope the query)

Rename `GetObjectiveTaskStatusesQuery`/Handler to `GetProjectTaskStatuses`, `ObjectiveId` param →
`ProjectId`. New body: look up the Project (`IProjectRepository.GetByIdForTenantAsync`, `NotFound` if
missing/inactive — mirror the null/active check style already used everywhere else in this module),
then `_statuses.GetProjectTemplateAsync(tenantId, project.Id, ct)` and map straight to
`TaskStatusResponse` — delete the lazy-copy-onto-Objective fallback entirely, it no longer applies.
Update the controller route and every caller (frontend is a separate Part in the frontend repo — this
backend Part just needs the new route to exist and work).

Tests: replace the existing "no rows yet → copies from template" test (no longer applicable — a Project
always already has its own template rows, seeded at creation) with "returns the Project's template rows
in `DisplayOrder`."

## Task 2: `CreateTaskStatusCommand`

`ObjectiveId` param → `ProjectId`. Handler: look up the Project instead of an Objective, resolve
`defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct)`, replace the
owner check with
`if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective!.Id, callerEmployeeId.Value, ct)) return Result<TaskStatusResponse>.Forbidden("Only an owner or member of this project can create task statuses.");`
(add `IMilestoneMembershipCoordinator` to the constructor — not currently present). Create the row with
`ProjectId = project.Id, ObjectiveId = null` instead of `ObjectiveId = objective.Id`.

Tests: update every existing test's setup to a Project instead of an Objective; add a case for a
non-owner default-Objective member succeeding (was previously impossible — plain Objective members
could never create statuses at all).

## Task 3: `EditTaskStatusCommandHandler`

Change the guard from `status.ObjectiveId is not null` (reject template rows) to
`status.ObjectiveId is null` (reject anything that ISN'T a template row — after Task 1-2 ship, no new
non-template rows are created, but old orphaned per-Objective copies from before this change still exist
in the database and must be explicitly rejected here, not silently edited). Replace the Objective lookup
+ owner check with a Project lookup (`_projects.GetByIdForTenantAsync(tenantId, status.ProjectId, ct)`)
+ default-Objective effective-manager check, same shape as Task 2. Add `IProjectRepository` and
`IMilestoneMembershipCoordinator` to the constructor (replacing the `IObjectiveRepository` dependency,
which this handler no longer needs — `grep` the file after this change to confirm `IObjectiveRepository`
has no other remaining use before removing the `using`/field/constructor param).

Tests: add a case asserting a per-Objective orphaned row (`ObjectiveId` set) returns `NotFound` even
though the `StatusId` is valid — this is the regression guard proving old orphaned rows stay
inaccessible, not silently reachable through the new endpoint.

## Task 4: `DeleteTaskStatusCommandHandler`

Same shape of change as Task 3 (guard flips to `status.ObjectiveId is null`, Project lookup +
default-Objective effective-manager check). Leave `_tasks.AnyActiveByStatusIdAsync` guard untouched.

## Task 5: `ReorderTaskStatusesCommandHandler`

`ObjectiveId` param → `ProjectId`. Same authorization shape as Task 2. Replace
`_statuses.GetByObjectiveIdAsync(tenantId, objective.Id, ct)` with
`_statuses.GetProjectTemplateAsync(tenantId, project.Id, ct)`. Everything else in this handler (the
exactly-one-`MarksTaskComplete` validation, the update loop) stays as-is — it doesn't reference
Objective-vs-Project anywhere else.

## Task 6: `MoveTaskStatusCommandHandler`

Change:
```csharp
if (newStatus is null || newStatus.ObjectiveId != task.ObjectiveId)
    return Result.NotFound("Target status not found.");
```
to:
```csharp
if (newStatus is null || newStatus.ProjectId != task.ProjectId)
    return Result.NotFound("Target status not found.");
```
(`WorkTask.ProjectId` already exists directly on the entity — confirmed in
`src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs`, no extra lookup needed). This is
independent of Part 1-6 of the cascading-ownership plan touching this same handler's owner-check block
(see that plan's Part 4 Task 3) — both changes land in the same file but at different lines; do this
Part's change regardless of which order the two plans are executed in, they don't conflict.

Tests: add a case where `newStatus.ProjectId == task.ProjectId` but the status is a template row that
was never tied to `task.ObjectiveId` under the old model — move succeeds (this is the actual point of
the fix: previously any status not copied onto the task's exact Objective was rejected as "not found,"
which no longer applies once every task in a Project shares the same status list).

## Task 7: Stop seeding per-Objective copies

- `CreateProjectCommandHandler.cs:282-286` — currently makes **two** `_taskStatuses.AddRangeAsync` calls:
  one with `objectiveId: null` (the template — keep this one, line 283-284), one with
  `objectiveId: defaultObjective.Id` (line 285-286, **remove this call entirely**).
- `CreateObjectiveCommandHandler.cs:129` — the whole
  `DefaultTaskStatusTemplate.BuildRows(tenantId, objective.ProjectId, objectiveId: objective.Id, userId, now)`
  seeding call for a newly-created sub-module — **remove entirely**, along with whatever surrounding
  `_taskStatuses.AddRangeAsync(...)` wrapper line calls it (read the surrounding ~5 lines in that file
  before deleting, to remove the whole statement cleanly, not just the inner `BuildRows` call).

Tests: `CreateProjectCommandHandlerTests` and `CreateObjectiveCommandHandlerTests` likely both assert
something about the created Objective having its own status rows today — find those assertions
(`grep -n "TaskStatus\|task.?status" tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateProjectCommandHandlerTests.cs tests/.../CreateObjectiveCommandHandlerTests.cs`
with the actual file path from the earlier `grep -rln`) and update or remove them to match the new
behavior — no per-Objective/per-sub-module rows are created anymore, only the Project template (already
seeded once, at Project creation, unchanged by this Task).

## Task 8: Full regression pass + Postman docs

1. `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`.
2. `dotnet build`.
3. `grep -rn "GetByObjectiveIdAsync" src/ONEVO.Application/Features/WorkManagement/Tasks/` — every
   remaining match should be for a genuinely Objective-scoped concept (Tasks themselves, not statuses);
   if any Task-Status-related handler still calls it, this Part missed a call site.
4. Update `docs/postman-request/Work Management/Create Task Status.md` (or whatever the current filenames
   are — `grep -rln "task-statuses\|Task Status" "docs/postman-request/Work Management/"`) for the new
   `projectId`-based routes and the new "any project owner/member" permission line, for all five
   endpoints touched in this Part.

## Definition of done

- Tasks 1-6 each committed individually.
- Task 7's regression pass is clean end to end, Postman docs updated.
- No task in this Part is "done" until an old orphaned per-Objective row (from before this change)
  demonstrably cannot be read or written through any of the five re-scoped endpoints.
