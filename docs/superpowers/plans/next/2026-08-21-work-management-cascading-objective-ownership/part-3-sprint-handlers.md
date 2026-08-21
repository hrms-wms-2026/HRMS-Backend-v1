# Part 3: Cascade ownership into Sprint create/edit/achieve/complete

**Read first:** `docs/superpowers/specs/next/2026-08-21-work-management-cascading-objective-ownership-design.md`
§4, and Part 1 in this same folder (adds `IsEffectiveManagerAsync`, must be done first). Independent of
Part 2 — can be done in either order relative to it, but Part 1 is a hard prerequisite for both.

**Scope guard:** Work Management module only.

## Goal

Same replacement as Part 2, applied to the four Sprint command handlers:
`objective.OwnerId != callerEmployeeId.Value` →
`!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct)`.

## Current state (verified by reading all four handlers directly)

- `CreateSprintCommandHandler.cs:49-50` — checks `objective.OwnerId != callerEmployeeId.Value`.
  Constructor currently: `ICurrentUser, ICallerIdentityResolver, IObjectiveRepository, ISprintRepository,
  IUnitOfWork` — **no** `IMilestoneMembershipCoordinator` yet, must be added.
- `EditSprintCommandHandler.cs:53-54` — checks `objective.OwnerId != callerEmployeeId.Value`.
  Constructor currently: `ICurrentUser, ICallerIdentityResolver, IObjectiveRepository, ISprintRepository,
  IUnitOfWork` — same, **no** `IMilestoneMembershipCoordinator` yet, must be added.
- `AchieveSprintCommandHandler.cs:63-64` — checks `objective.OwnerId != callerEmployeeId.Value`.
  **Already has** `IMilestoneMembershipCoordinator _membership` injected (currently used only for
  `_membership.GetActiveAssigneeAsync` when building the notification recipient list — that usage is
  unrelated and stays untouched).
- `CompleteSprintCommandHandler.cs:74-75` — checks `objective.OwnerId != callerEmployeeId.Value`.
  **Already has** `_membership` injected, same as Achieve — used for notifications, untouched.

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint/CreateSprintCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/EditSprint/EditSprintCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/AchieveSprint/AchieveSprintCommandHandler.cs`
- `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/CompleteSprintCommandHandler.cs`
- Matching test files — `grep -rln "CreateSprintCommandHandlerTests\|EditSprintCommandHandlerTests\|AchieveSprintCommandHandlerTests\|CompleteSprintCommandHandlerTests" tests/`.

## Task 1: `CreateSprintCommandHandler`

1. Add `IMilestoneMembershipCoordinator membership` to the constructor, store as `_membership`.
2. Replace:
   ```csharp
   if (objective.OwnerId != callerEmployeeId.Value)
       return Result<SprintResponse>.Forbidden("Only this milestone's owner can create sprints.");
   ```
   with:
   ```csharp
   if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
       return Result<SprintResponse>.Forbidden("Only this milestone's owner can create sprints.");
   ```

Tests: add a case where the caller is an active member of the objective's parent (not owner of the
objective itself) — Sprint creation now succeeds. Keep the existing unrelated-caller-Forbidden test.

## Task 2: `EditSprintCommandHandler`

Same two changes as Task 1 (add `_membership` to constructor; replace the owner check), against:
```csharp
if (objective.OwnerId != callerEmployeeId.Value)
    return Result<SprintResponse>.Forbidden("Only this milestone's owner can edit sprints.");
```
Same test additions as Task 1.

## Task 3: `AchieveSprintCommandHandler`

No constructor change needed (`_membership` already present). Replace:
```csharp
if (objective.OwnerId != callerEmployeeId.Value)
    return Result<SprintResponse>.Forbidden("Only this milestone's owner can achieve sprints.");
```
with:
```csharp
if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
    return Result<SprintResponse>.Forbidden("Only this milestone's owner can achieve sprints.");
```
Same test additions as Task 1.

## Task 4: `CompleteSprintCommandHandler`

No constructor change needed. Replace:
```csharp
if (objective.OwnerId != callerEmployeeId.Value)
    return Result<SprintResponse>.Forbidden("Only this milestone's owner can complete sprints.");
```
with:
```csharp
if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
    return Result<SprintResponse>.Forbidden("Only this milestone's owner can complete sprints.");
```
Same test additions as Task 1. Leave the pre-existing task-status-completeness check
(`Every task in this sprint must be in a complete status...`, 422) completely untouched — it runs
independently, after this authorization check.

## Task 5: Full regression pass for this Part

Run `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`. Run
`grep -rn "objective.OwnerId != callerEmployeeId" src/ONEVO.Application/Features/WorkManagement/Sprints/`
— should return nothing.

## Definition of done

- Tasks 1-4 each committed individually, Task 5 is a verification step only.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green.
- `dotnet build` compiles clean.
- The grep in Task 5 returns nothing.
