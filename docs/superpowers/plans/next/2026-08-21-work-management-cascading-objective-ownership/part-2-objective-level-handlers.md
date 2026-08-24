# Part 2: Cascade ownership into sub-module create/edit + member management

**Read first:** `docs/superpowers/specs/next/2026-08-21-work-management-cascading-objective-ownership-design.md`
§4, and Part 1 in this same folder — this Part calls the `IsEffectiveManagerAsync` method Part 1 adds to
`IMilestoneMembershipCoordinator`. Part 1 must be done and merged before starting this Part.

**Scope guard:** Work Management module only.

**Status:** shipped 2026-08-21. Plan's 4 handlers plus 4 more the plan's own mandated grep caught
(`CreateObjective`, `EditObjective`, `AddObjectiveMember`, `RemoveObjectiveMember`,
`AchieveObjective`, `UnachieveObjective`, `DeleteObjective`, `TransferObjectiveHead`) converted to the
cascading effective-manager check, one commit each.

## Goal

Replace the direct-match `objective.OwnerId != callerEmployeeId.Value` check with
`!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct)` in the
four Objective-level command handlers below. `IsEffectiveManagerAsync`'s signature (from Part 1):
`Task<bool> IsEffectiveManagerAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)`.

## Current state (verified by reading all four handlers directly)

- `CreateObjectiveCommandHandler.cs:64-65` — checks `parent.OwnerId != callerEmployeeId.Value` (note:
  the variable is named `parent`, not `objective` — this is the parent Objective the new sub-module is
  being created under). Already has `IMilestoneMembershipCoordinator _membership` injected (constructor
  param `membership`).
- `EditObjectiveCommandHandler.cs:61-62` — checks `objective.OwnerId != callerEmployeeId.Value`. Does
  **not** have `IMilestoneMembershipCoordinator` injected yet — constructor currently takes
  `ICurrentUser, ICallerIdentityResolver, IObjectiveRepository, IObjectiveChangeRequestRepository,
  IUnitOfWork` only.
- `AddObjectiveMemberCommandHandler.cs:57-58` — checks `objective.OwnerId != callerEmployeeId.Value`.
  Already has `_membership` injected.
- `RemoveObjectiveMemberCommandHandler.cs:55-56` — checks `objective.OwnerId != callerEmployeeId.Value`.
  Already has `_membership` injected. Note: line 58 has a **separate**, unrelated check
  (`if (request.EmployeeId == objective.OwnerId)`, guarding against removing the exact-node owner as a
  member) — do not touch that line, it is a different rule.

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective/EditObjectiveCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs`
- Matching test files — `grep -rln "CreateObjectiveCommandHandlerTests\|EditObjectiveCommandHandlerTests\|AddObjectiveMemberCommandHandlerTests\|RemoveObjectiveMemberCommandHandlerTests" tests/` to find exact paths (not enumerated here — this project's test file layout for this feature area wasn't traced file-by-file during design).

## Task 1: `CreateObjectiveCommandHandler`

Replace:
```csharp
if (parent.OwnerId != callerEmployeeId.Value)
    return Result<ObjectiveDetailResponse>.Forbidden("Only the parent milestone's head can create a sub-milestone under it.");
```
with:
```csharp
if (!await _membership.IsEffectiveManagerAsync(tenantId, parent.Id, callerEmployeeId.Value, ct))
    return Result<ObjectiveDetailResponse>.Forbidden("Only the parent milestone's head can create a sub-milestone under it.");
```
(keep the existing error message — only the condition changes). Confirm `tenantId` and `ct` are already
in scope as local variables at this point in the method (they are, used by surrounding lines).

Tests: add a case where the caller is not `parent.OwnerId` but is an active member of `parent`'s
**parent** (i.e. two levels up) — creation now succeeds. Keep the existing "unrelated caller → Forbidden"
test as a regression check.

## Task 2: `EditObjectiveCommandHandler`

1. Add `IMilestoneMembershipCoordinator membership` to the constructor, store as `_membership`
   (`using ONEVO.Application.Features.WorkManagement.Objectives.Services;` if not already imported —
   check the existing `using` block first, `CreateObjectiveCommandHandler.cs` in the same folder already
   has it and can be used as reference for the exact namespace).
2. Replace:
   ```csharp
   if (objective.OwnerId != callerEmployeeId.Value)
       return Result<ObjectiveEditOutcomeResponse>.Forbidden("Only this milestone's head can edit it.");
   ```
   with:
   ```csharp
   if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
       return Result<ObjectiveEditOutcomeResponse>.Forbidden("Only this milestone's head can edit it.");
   ```

Tests: same shape as Task 1 — add a grandparent-member-can-now-edit case, keep the unrelated-caller
regression test. Also update this test file's constructor calls for `EditObjectiveCommandHandler` to
supply the new `membership` mock parameter — every existing test in the file will fail to compile
otherwise.

## Task 3: `AddObjectiveMemberCommandHandler`

Replace:
```csharp
if (objective.OwnerId != callerEmployeeId.Value)
    return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Only this milestone's head can add members.");
```
with:
```csharp
if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
    return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Only this milestone's head can add members.");
```

Tests: same shape as Task 1.

## Task 4: `RemoveObjectiveMemberCommandHandler`

Replace:
```csharp
if (objective.OwnerId != callerEmployeeId.Value)
    return Result.Forbidden("Only this milestone's head can remove members.");
```
with:
```csharp
if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
    return Result.Forbidden("Only this milestone's head can remove members.");
```
Leave the following `if (request.EmployeeId == objective.OwnerId)` block completely untouched — it's a
different rule (can't remove the objective's own designated owner via this path).

Tests: same shape as Task 1.

## Task 5: Full regression pass for this Part

Run `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` and read the output.
Run `grep -rn "objective.OwnerId != callerEmployeeId\|parent.OwnerId != callerEmployeeId"
src/ONEVO.Application/Features/WorkManagement/Objectives/` — should return nothing (confirms no
Objective-level handler was missed in this Part; Sprint/Task handlers are intentionally still unconverted
until Parts 3-4).

## Definition of done

- Tasks 1-4 each committed individually (one commit per handler), Task 5 is a verification step, not its
  own commit.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green.
- `dotnet build` compiles clean.
- The grep in Task 5 returns nothing.
