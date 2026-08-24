# Part 2: New "Get Tasks by Sprint" endpoint

**Read first:** `docs/superpowers/specs/next/2026-08-20-work-management-tree-sprint-task-unified-view-design.md`
§2-3. Independent of Part 1, but **build this handler with the reachability check from the start** —
don't copy the pre-fix version of `GetObjectiveTasksQueryHandler`, copy the Part-1-fixed version's
pattern (or `GetObjectiveSubtreeQueryHandler` directly if Part 1 hasn't landed yet in your working copy).

**Scope guard:** Work Management module only.

**Status:** done, backend and frontend both shipped 2026-08-21 (frontend consumes `GET
/work/sprints/{id}/tasks` via the unified tree's lazy Sprint/Task nesting).

## Goal

The tree's "expand a Sprint node → see its Tasks" step needs a scoped, per-sprint task list. Today only
`GetObjectiveTasksQuery` exists (all tasks for an objective, client-filtered by `sprintId`). The
repository method to do this server-side, `IWorkTaskRepository.GetBySprintIdAsync(tenantId, sprintId,
ct)`, already exists but has no query/handler/endpoint calling it. Wire it up.

## Files to create

- `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetSprintTasks/GetSprintTasksQuery.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetSprintTasks/GetSprintTasksQueryHandler.cs`
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetSprintTasksQueryHandlerTests.cs`
- `docs/postman-request/Work Management/Get Sprint Tasks.md`

## Files to modify

- `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` — new action, or
  `SprintsController.cs` if this repo's routing convention puts sprint-scoped reads there instead (check
  which controller owns `sprints/{id}/...` routes first — `SprintsController.cs` already has
  `[HttpPatch("sprints/{id:guid}")]` for Edit, so a `[HttpGet("sprints/{id:guid}/tasks")]` there matches
  the existing route-ownership convention better than adding it to `TasksController`).

## Before writing code

Read `GetObjectiveTasksQueryHandler.cs` (post-Part-1, if landed) for the response-shaping pattern
(assignee lookup via `ITaskAssignmentRepository.GetByTaskIdsAsync`) — this new handler needs the identical
assignee-population logic, just sourcing tasks from `GetBySprintIdAsync` instead of
`GetByObjectiveIdAsync`. Also read `ISprintRepository`'s existing get-by-id method (find it — likely
`GetByIdForTenantAsync` or similar) since this handler needs to load the Sprint first to find its
`ObjectiveId` (the reachability check is objective-based, not sprint-based — a Sprint doesn't carry
membership info itself).

## Tasks (small, do in order, one commit per task)

1. **`GetSprintTasksQuery`**: `public sealed record GetSprintTasksQuery(Guid SprintId) :
   IRequest<Result<IReadOnlyList<WorkTaskResponse>>>;` — reuse the existing `WorkTaskResponse` DTO as-is,
   no new response shape needed.

2. **`GetSprintTasksQueryHandler`**:
   - Authenticate, resolve `callerEmployeeId`.
   - Load the Sprint via the repository (`NotFound` if missing) to get its `ObjectiveId`.
   - Load that Objective via `IObjectiveRepository.GetByIdForTenantAsync` (`NotFound` if somehow missing —
     defensive, shouldn't happen given the FK).
   - Apply the **identical** reachability check from Part 1 (tenant permission bypass, else self+ancestor
     membership walk) using the Sprint's Objective.
   - On success: `_tasks.GetBySprintIdAsync(tenantId, request.SprintId, ct)`, then the same
     assignee-population logic as `GetObjectiveTasksQueryHandler`, map to `WorkTaskResponse` list.
   - Tests: (a) member of the sprint's objective → success, correct tasks returned (only tasks with this
     `SprintId`, prove it's actually filtered server-side and not returning everything); (b) member of an
     **ancestor** of the sprint's objective → success; (c) no membership/no permission → `Forbidden`;
     (d) nonexistent `SprintId` → `NotFound`; (e) assignees populate correctly (mirror
     `GetObjectiveTasksQueryHandlerTests`'s existing assignee test).

3. **Controller action**: `[HttpGet("sprints/{id:guid}/tasks")] [RequirePermission("projects:access")]`
   on whichever controller owns sprint routes (see "Before writing code" above), calling
   `new GetSprintTasksQuery(id)`.

4. **Postman doc**: `docs/postman-request/Work Management/Get Sprint Tasks.md`, full 6-section format,
   Source section linking this plan file and the new handler/controller files.

## Data flow

`GET /work/sprints/{sprintId}/tasks` → handler loads Sprint → loads its Objective → reachability check
(self+ancestor membership OR tenant `projects:read`/`*`) → `IWorkTaskRepository.GetBySprintIdAsync` →
assignee lookup → `WorkTaskResponse` list, scoped to exactly that sprint's tasks. This is what the tree's
"expand Sprint" step calls on first expand (frontend plan, written separately).

## Security

Same reachability model as Part 1, applied via the sprint's parent Objective — never grant access based
on the Sprint alone (a Sprint has no membership concept of its own, per the earlier Explore findings; all
Work Management membership is Objective-scoped).

## Definition of done

- All 4 tasks committed individually.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green.
- Full solution `dotnet build` compiles clean.
- `docs/postman-request/Work Management/Get Sprint Tasks.md` created, accurate to the real DTO.
- Once both Part 1 and Part 2 are done, this whole `2026-08-20-work-management-tree-sprint-task-unified-view/`
  folder stays in `plans/next/` (status note: "backend-done, frontend-pending") until the frontend plan
  also ships, same convention as the Project Page Redesign plan.
