# Part 1: Fix missing authorization on Get Sprints/Tasks-by-Objective

**Read first:** `docs/superpowers/specs/next/2026-08-20-work-management-tree-sprint-task-unified-view-design.md`
§2. This is a **security fix**, independent of Part 2 — do it first regardless.

**Scope guard:** Work Management module only. Do not touch other modules.

**Status:** done, backend and frontend both shipped 2026-08-21 (frontend consumes the now-authorized
endpoints via the unified tree's lazy Sprint/Task nesting).

## Goal

`GetObjectiveSprintsQueryHandler` and `GetObjectiveTasksQueryHandler` (routes `GET
/work/objectives/{objectiveId}/sprints` and `GET /work/objectives/{objectiveId}/tasks`) currently check
only `_currentUser.IsAuthenticated` — **any authenticated tenant user who knows/guesses an objectiveId can
read that objective's full sprint/task list**, regardless of whether they have any relationship to that
objective or project. Every sibling read endpoint in this module (`GetObjectiveSubtreeQueryHandler`,
`GetObjectiveByIdQueryHandler`) enforces a reachability check. Add the identical check to both handlers.

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetObjectiveSprints/GetObjectiveSprintsQueryHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTasks/GetObjectiveTasksQueryHandler.cs`
- `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (the `GetByObjective` action)
- `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (the `GetByObjective` action)
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetObjectiveTasksQueryHandlerTests.cs` (existing —
  its two tests construct `GetObjectiveTasksQueryHandler` directly; their constructor call needs updating
  once new dependencies are added, plus new Forbidden-path tests)

## Files to create

- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/GetObjectiveSprintsQueryHandlerTests.cs` (none
  exists today for this handler)

## Before writing code

Read `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs`
in full — this is the **exact** pattern to copy (not `GetObjectiveTreeQueryHandler`'s different BFS
pattern; the Tree tab the frontend actually uses calls `GetSubtree`, so visibility must stay consistent
with that one specifically). The relevant block, lines 42-70 of that file:
1. Resolve `callerEmployeeId` via `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync`.
2. Load the objective via `IObjectiveRepository.GetByIdForTenantAsync`; `NotFound` if missing.
3. Check tenant-wide permission bypass: `IPermissionResolver.ResolveAsync(userId, tenantId, null, ct)`,
   `permissions.Contains("projects:read") || permissions.Contains("*")`.
4. If no bypass: walk `ParentObjectiveId` up to build `selfAndAncestorIds`, then
   `IProjectMemberRepository.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId,
   callerEmployeeId.Value, selfAndAncestorIds, ct)` — `Forbidden` if false.

## Tasks (small, do in order, one commit per task)

1. **`GetObjectiveSprintsQueryHandler`**: add constructor dependencies `ICallerIdentityResolver`,
   `IObjectiveRepository`, `IProjectMemberRepository`, `IPermissionResolver` (alongside the existing
   `ICurrentUser`, `ISprintRepository`). At the top of `Handle`, after the existing `IsAuthenticated`
   check, insert the exact 4-step block described above using `request.ObjectiveId`, returning
   `Result<IReadOnlyList<SprintResponse>>.NotFound`/`.Forbidden` as appropriate before the existing
   sprint-fetch logic runs.
   - Tests (new file): (a) caller with active membership on the objective itself → success, returns
     sprints; (b) caller with active membership only on an **ancestor** objective → success (proves the
     ancestor-walk works, not just exact-match); (c) caller with `projects:read` tenant permission but no
     membership → success (bypass path); (d) caller authenticated but with **no** membership anywhere in
     the chain and no tenant permission → `Forbidden`; (e) nonexistent `ObjectiveId` → `NotFound`.

2. **`GetObjectiveTasksQueryHandler`**: identical change — same 4 new dependencies, same 4-step check
   inserted before the existing `_tasks.GetByObjectiveIdAsync` call.
   - Update the 2 existing tests in `GetObjectiveTasksQueryHandlerTests.cs`: their handler-construction
     call needs the new mocked dependencies (mock each to represent "caller has active membership on
     `ObjectiveId` itself" so the existing happy-path assertions keep passing unmodified — don't change
     what they assert, just make them compile again with the new constructor shape). Add the same 5
     regression cases as Task 1 (member on objective / member on ancestor / permission bypass / forbidden
     / not-found), mirroring the new Sprints test file's structure.

3. **Controller attributes**: add `[RequirePermission("projects:access")]` above both `GetByObjective`
   actions in `SprintsController.cs` and `TasksController.cs`, matching `ObjectivesController.GetSubtree`'s
   exact convention (the sibling endpoint this feature pairs with).
   - No new test needed here — covered by the handler tests plus whatever existing controller-attribute
     convention this repo already relies on for `[RequirePermission]` enforcement (it's tested at the
     middleware level elsewhere in this codebase, not per-controller).

4. **Postman docs**: update `docs/postman-request/Work Management/` entries for these two endpoints (find
   them — likely `Get Objective Sprints.md` / `Get Objective Tasks.md` or similar; grep for the route) to
   note the auth/permission line now matches the reachability-checked pattern, per rule 6. If no such
   doc exists yet for either endpoint, create it.

## Data flow

No change to the happy path's data flow — a caller who already has legitimate access sees the same
response as before. The only behavioral change is that a caller with **no** relationship to the objective
now correctly gets `403 Forbidden` instead of a full data dump.

## Security

This *is* the security fix — see Goal. Verify manually (via the new tests, not a live click-through) that
the fix doesn't accidentally also lock out the two legitimate paths that must keep working: (1) a plain
project member who is active on the objective itself, and (2) — important for the tree feature this
unblocks — a member of an **ancestor** objective (e.g. the Default Objective owner) expanding a
descendant leaf's Sprints/Tasks, which is exactly the scenario `GetObjectiveSubtreeQueryHandler`'s
ancestor-walk already supports for Objectives and must now also work for Sprints/Tasks.

## Definition of done

- All 4 tasks committed individually.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green — pay particular
  attention to the new Forbidden/ancestor-membership regression tests, they're the point of this part.
- Full solution `dotnet build` compiles clean.
- Postman docs updated/created for both endpoints.
