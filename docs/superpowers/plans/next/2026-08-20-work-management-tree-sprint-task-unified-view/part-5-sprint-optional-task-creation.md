# Part 5: Make Sprint optional when creating a Task

**Read first:** `docs/superpowers/specs/next/2026-08-20-work-management-tree-sprint-task-unified-view-design.md`
§4 — the new tree UI adds an "Add task" icon directly on Module rows (not just Sprint rows), so a Task must
be creatable with no Sprint at all, through both the owner-direct path and the non-owner-request path.

**Scope guard:** Work Management module only.

**Status:** shipped 2026-08-21 (Part 5 Tasks 1-4). `SprintId` is optional on CreateTask, CreateTaskCreationRequest, and ApproveTaskCreationRequest.

## Goal

`SprintId` is currently a required, non-nullable `Guid` at three independent layers: the owner-direct
create command, the non-owner create-request command, and the later approval of that request. All three
must independently tolerate a null Sprint — they are three separate code paths, not one shared function, so
fixing the validator on `CreateTaskCommand` alone does **not** fix task-creation-request approval.

## Current state (verified by reading all three handlers directly)

- `CreateTaskCommand.SprintId` is `Guid` (non-nullable). Validator:
  `RuleFor(x => x.SprintId).NotEqual(Guid.Empty).WithMessage("Sprint is required.");`
- `CreateTaskCommandHandler` unconditionally looks up the sprint and rejects if missing/achieved:
  ```csharp
  var sprint = await _sprints.GetByIdForTenantAsync(tenantId, request.SprintId, ct);
  if (sprint is null || sprint.ObjectiveId != objective.Id)
      return Result<WorkTaskResponse>.NotFound("Sprint not found.");
  if (sprint.Status == SprintStatuses.Achieved)
      return Result<WorkTaskResponse>.Conflict("This sprint has been achieved and is frozen.");
  ```
  then sets `SprintId = request.SprintId` on the new `WorkTask`.
- `CreateTaskCreationRequestCommand.SprintId` (`Guid`), its validator, and its handler repeat the **exact
  same** shape independently — this is the non-owner request-for-approval path, storing the payload as
  `TaskCreationRequestPayload.SprintId` (`Guid`, non-nullable).
- `ApproveTaskCreationRequestCommandHandler` deserializes that payload and repeats the sprint
  lookup/frozen-check **a third time**, independently, when actually constructing the `WorkTask` on
  approval.
- **Status resolution is already sprint-independent**: `TaskStatus` has no `SprintId` at all — it's scoped
  per-Project (template) / per-Objective (live copy) only. Both handlers resolve the default status purely
  from `_statuses.GetByObjectiveIdAsync(tenantId, objective.Id, ct)`. A sprint-less task creation does not
  need any change here — this confirms the fix is scoped exactly to the three sprint-lookup blocks above,
  nothing else in either handler depends on Sprint.
- The **output** side already tolerates null: `WorkTaskResponse.SprintId` and `WorkTaskViewModel.SprintId`
  are already `Guid?`. Only the *input* commands/validators/payload force it non-nullable.

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommand.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandValidator.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCreationRequest/CreateTaskCreationRequestCommand.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCreationRequest/CreateTaskCreationRequestCommandValidator.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCreationRequest/CreateTaskCreationRequestCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/TaskCreationRequestPayload.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs`
- `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`CreateTaskRequest` — and the
  request-creation equivalent contract in the same file if it's separate)
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs`,
  `CreateTaskCreationRequestCommandHandlerTests.cs`, `ApproveTaskCreationRequestCommandHandlerTests.cs` (or
  wherever these existing test files live — `grep -rl CreateTaskCommandHandlerTests tests/`)

## Tasks (small, do in order, one commit per task — each of the three paths is independent, do them in this
order so tests catch regressions early on the simplest path first)

1. **`CreateTaskCommand` path**:
   - Change `Guid SprintId` → `Guid? SprintId` on `CreateTaskCommand`.
   - Validator: delete the `NotEqual(Guid.Empty)` rule entirely (a `Guid?` that's `null` needs no
     "required" rule; if you want to keep validating a *non-null-but-empty* `Guid.Empty` accidentally sent
     by a buggy client, use `.Must(id => id is null || id != Guid.Empty)` instead — check whether other
     nullable-Guid fields elsewhere in this validator file already have this defensive pattern and match
     it for consistency, otherwise skip it, don't invent a new convention).
   - Handler: wrap the sprint lookup in `if (request.SprintId is not null) { ... same lookup/frozen-check
     ... }` — when null, skip straight to constructing the task with `SprintId = null`.
   - `CreateTaskRequest` (API contract) and the controller binding: `Guid SprintId` → `Guid? SprintId`.
   - Tests: (a) existing "sprint required" test is deleted/replaced since it's no longer true; (b) new test:
     create with `SprintId = null` under a Module directly → success, resulting task has `SprintId == null`;
     (c) create with a valid `SprintId` → unchanged existing behavior (regression check); (d) create with an
     achieved sprint's id → still `Conflict` (regression check — the frozen-check must still fire when a
     sprint IS provided).

2. **`CreateTaskCreationRequestCommand` path**: identical shape of changes to `CreateTaskCreationRequestCommand`,
   its validator, its handler, and `TaskCreationRequestPayload.SprintId` (`Guid` → `Guid?`). Same test
   additions (null-sprint request succeeds and is stored with `SprintId: null` in the persisted
   `PayloadJson` — assert this by deserializing `pending.PayloadJson` in the test, not just checking the
   command returned success).

3. **`ApproveTaskCreationRequestCommandHandler` path**: this is the path most likely to be missed since it's
   not reached by calling either Create command directly — it only runs when a *pending* request (created in
   task 2) is approved. Update it to deserialize `payload.SprintId` as `Guid?` and wrap its own independent
   sprint lookup/frozen-check the same way as task 1. Test: create a task-creation-request with
   `SprintId = null` (task 2's new capability), then approve it, assert the resulting `WorkTask.SprintId ==
   null` and no `NotFound`/`Conflict` is thrown from a lookup against a null id.

4. **Full-module regression pass**: after all three paths are updated, run the complete WorkManagement test
   filter once and read the output rather than trusting each task's local green — a shared payload/DTO
   change across three handlers is exactly the kind of change that can pass each handler's own tests while
   breaking an integration point between them (e.g. a mapper or another handler that also deserializes
   `TaskCreationRequestPayload` and wasn't in this plan's file list — grep
   `grep -rln TaskCreationRequestPayload src/` before declaring this task done, to make sure no fourth call
   site was missed).

## Definition of done

- All 4 tasks committed individually.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green.
- Full solution `dotnet build` compiles clean.
- `grep -rn "Sprint is required" src/` returns nothing (confirms the old validator message is fully gone,
  not just unreachable in one of the three paths).
- Postman docs updated: `docs/postman-request/Work Management/Create Task.md` and `Create Task Creation
  Request.md` (find their exact current filenames under `docs/postman-request/Work Management/` — request
  examples must show `sprintId` as optional/nullable, not required).
