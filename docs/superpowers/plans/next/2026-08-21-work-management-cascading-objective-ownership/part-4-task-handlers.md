# Part 4: Cascade ownership into Task create/delete + the private-status move gate

**Read first:** `docs/superpowers/specs/next/2026-08-21-work-management-cascading-objective-ownership-design.md`
§4, and Part 1 in this same folder (adds `IsEffectiveManagerAsync`, must be done first). Independent of
Parts 2-3 — can be done in any order relative to them, but Part 1 is a hard prerequisite for all three.

**Scope guard:** Work Management module only.

## Goal

Same replacement pattern as Parts 2-3, applied to `CreateTask` and `DeleteTask`. `MoveTaskStatus` is
different — it's a two-branch check today (owner bypasses everything; a plain member may move into any
`public` status but not `private`) and becomes: an effective manager (owner **or** any-ancestor
owner/member) bypasses everything, exactly like before; a plain non-cascaded member keeps today's
public-only restriction.

## Current state (verified by reading all three handlers directly)

- `CreateTaskCommandHandler.cs:61-62` — checks `objective.OwnerId != callerEmployeeId.Value`, message
  explicitly mentions the request-workflow fallback ("Non-owner members must submit a task creation
  request."). Constructor has **no** `IMilestoneMembershipCoordinator` yet.
- `DeleteTaskCommandHandler.cs:48-49` — checks `objective.OwnerId != callerEmployeeId.Value`.
  Constructor has **no** `IMilestoneMembershipCoordinator` yet.
- `MoveTaskStatusCommandHandler.cs:71-82` — **already has** `_membership` injected (used today for
  `_membership.IsActiveMemberAsync`, not `IsEffectiveManagerAsync` — that's the new Part-1 method). Exact
  current code:
  ```csharp
  if (objective.OwnerId != callerEmployeeId.Value)
  {
      var isMember = await _membership.IsActiveMemberAsync(
          tenantId,
          objective.Id,
          callerEmployeeId.Value,
          ct);
      if (!isMember)
          return Result.Forbidden("Only active milestone members can move tasks.");
      if (newStatus.Visibility == TaskStatusVisibilities.Private)
          return Result.Forbidden("Only the milestone owner can move a task into this status.");
  }
  ```

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTask/DeleteTaskCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`
- Matching test files — `grep -rln "CreateTaskCommandHandlerTests\|DeleteTaskCommandHandlerTests\|MoveTaskStatusCommandHandlerTests" tests/`.

## Task 1: `CreateTaskCommandHandler`

1. Add `IMilestoneMembershipCoordinator membership` to the constructor, store as `_membership`.
2. Replace:
   ```csharp
   if (objective.OwnerId != callerEmployeeId.Value)
       return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can create tasks directly. Non-owner members must submit a task creation request.");
   ```
   with:
   ```csharp
   if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
       return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can create tasks directly. Non-owner members must submit a task creation request.");
   ```
   Keep the existing error message as-is even though it now also covers cascaded managers — it's still
   accurate for the caller who actually hits this branch (someone with no owner/manager relationship at
   any level).

Tests: add a case where the caller is a plain (non-owner) active member of a **grandparent** Objective —
task creation now succeeds directly, no request needed. Keep the existing "plain member of the exact
objective, not owner, no ancestor relationship → Forbidden, must use request path" test as a regression
check — this is the one case that must still fail after this change (same-node plain membership alone
does not grant direct rights, only cascade-from-ancestor does; re-read
`2026-08-21-work-management-cascading-objective-ownership-design.md` §2 if this distinction is unclear
before writing the test).

## Task 2: `DeleteTaskCommandHandler`

1. Add `IMilestoneMembershipCoordinator membership` to the constructor, store as `_membership`.
2. Replace:
   ```csharp
   if (objective.OwnerId != callerEmployeeId.Value)
       return Result.Forbidden("Only this milestone's owner can delete tasks.");
   ```
   with:
   ```csharp
   if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
       return Result.Forbidden("Only this milestone's owner can delete tasks.");
   ```

Tests: same shape as Task 1 (grandparent-member-succeeds case; keep same-node-plain-member-still-forbidden
regression case).

## Task 3: `MoveTaskStatusCommandHandler`

Replace the whole block quoted in "Current state" above with:
```csharp
if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
{
    var isMember = await _membership.IsActiveMemberAsync(
        tenantId,
        objective.Id,
        callerEmployeeId.Value,
        ct);
    if (!isMember)
        return Result.Forbidden("Only active milestone members can move tasks.");
    if (newStatus.Visibility == TaskStatusVisibilities.Private)
        return Result.Forbidden("Only the milestone owner can move a task into this status.");
}
```
This preserves the exact same fallback shape (plain same-node member: public-only), just swaps the
top-level bypass condition from "is the exact-node owner" to "is an effective manager (owner or ancestor
owner/member)."

Tests: this is the specific case from the requirements chat — add a test where the caller is a plain
(non-owner) active member of the objective's **parent**, not the exact objective's owner, and the target
status has `Visibility == Private` — the move must now succeed (this is new behavior this Part adds).
Keep the existing tests: same-node owner can always move (regression), same-node plain member can move
into `public` (regression), same-node plain member cannot move into `private` (regression — this is the
"no ancestor relationship" case, must still be Forbidden), unrelated caller with no membership at all is
still Forbidden with "Only active milestone members..." (regression).

## Task 4: Full regression pass for this Part

Run `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`. Run
`grep -rn "objective.OwnerId != callerEmployeeId" src/ONEVO.Application/Features/WorkManagement/Tasks/`
— should return nothing (`AssignTask`/`UnassignTask`/the `*TaskCreationRequest`/`*TaskEditRequest`
handlers and `CreateTaskStatus`/`EditTaskStatus`/`DeleteTaskStatus`/`ReorderTaskStatuses` are believed to
use a different pattern or none at all — this grep confirms that belief rather than assuming it; if it
does turn up a match this Part's research missed, add the same fix here before calling this Part done).

## Definition of done

- Tasks 1-3 each committed individually, Task 4 is a verification step only.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green.
- `dotnet build` compiles clean.
- The grep in Task 4 returns nothing (or any surprise matches were fixed and re-verified).
