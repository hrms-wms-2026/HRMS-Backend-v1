# Part 1: Add `IsEffectiveManagerAsync` to `IMilestoneMembershipCoordinator`

**Read first:** `docs/superpowers/specs/next/2026-08-21-work-management-cascading-objective-ownership-design.md`
§3 — this Part builds the one shared helper every later Part (2-5) calls instead of repeating
`objective.OwnerId != callerEmployeeId.Value`.

**Scope guard:** Work Management module only.

**Status:** shipped 2026-08-21. `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync` added (owner or
active member of the Objective or any ancestor, walking `ParentObjectiveId` up); every later Part (2-5)
calls it.

## Goal

Today, "can this caller manage this Objective" is checked inline, ad hoc, at ~9 different call sites,
always as `objective.OwnerId != callerEmployeeId.Value` — a direct match against the single-owner field,
no cascade. Add one method that instead answers: is the caller the `OwnerId` of this Objective, or an
active `ProjectMember` of it, or (recursively) the `OwnerId`/an active member of **any ancestor**
Objective, walking up via `ParentObjectiveId`. Rights flow down only — an ancestor gets nothing back
from a descendant's ownership, so this method never looks at descendants, only self + ancestors.

## Current state (verified by reading the files directly)

- `IMilestoneMembershipCoordinator` / `MilestoneMembershipCoordinator`
  (`src/ONEVO.Application/Features/WorkManagement/Objectives/Services/`) already has
  `IsActiveMemberAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct)`, which
  checks `ListActiveForObjectiveAsync` for a matching `EmployeeId` — exact-node-only, no ancestor walk.
- The coordinator's constructor currently takes only `IEmployeeRepository` and `IProjectMemberRepository`.
  It does **not** have `IObjectiveRepository` — this Part adds that dependency.
- `IObjectiveRepository.GetByIdForTenantAsync(Guid tenantId, Guid objectiveId, CancellationToken ct)`
  returns `Objective?` and is already used everywhere (e.g.
  `CreateSprintCommandHandler.cs:45`). `Objective.ParentObjectiveId` is `Guid?`, `Objective.OwnerId` is
  `Guid`.
- The exact ancestor-walk pattern this Part's implementation should follow already exists inline in
  `GetObjectiveSprintsQueryHandler.cs:61-71` — reuse that shape, don't invent a different one.

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IMilestoneMembershipCoordinator.cs`
- `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs`
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/MilestoneMembershipCoordinatorTests.cs`

## Task 1: Add the interface method

In `IMilestoneMembershipCoordinator.cs`, add:

```csharp
Task<bool> IsEffectiveManagerAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);
```

## Task 2: Implement it

In `MilestoneMembershipCoordinator.cs`:

1. Add a constructor dependency `IObjectiveRepository _objectives` (new `using
   ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;`), alongside the existing
   `_employees` and `_members`.
2. Implement:

```csharp
public async Task<bool> IsEffectiveManagerAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default)
{
    var cursor = await _objectives.GetByIdForTenantAsync(tenantId, objectiveId, ct);

    while (cursor is not null)
    {
        if (cursor.OwnerId == employeeId)
            return true;

        if (await IsActiveMemberAsync(tenantId, cursor.Id, employeeId, ct))
            return true;

        cursor = cursor.ParentObjectiveId is null
            ? null
            : await _objectives.GetByIdForTenantAsync(tenantId, cursor.ParentObjectiveId.Value, ct);
    }

    return false;
}
```

This reuses the existing `IsActiveMemberAsync` for the membership check at each level, so there is one
single source of truth for "is this employee an active member of this exact Objective."

## Task 3: Update the test file's DI setup

`MilestoneMembershipCoordinatorTests.BuildCoordinator` currently constructs
`new MilestoneMembershipCoordinator(employees.Object, members.Object)` — add a third
`Mock<IObjectiveRepository>` parameter (default to a mock with no setups, since most existing tests in
this file don't touch Objectives at all) and pass `objectives.Object` as the third constructor arg. Every
existing call site of `BuildCoordinator` in this file must still compile — check whether any of them
need to also supply an `objectives` mock explicitly, or whether the default no-op mock is enough (it is,
for every existing test — none of them call `IsEffectiveManagerAsync`).

## Task 4: Write the new tests

Add to `MilestoneMembershipCoordinatorTests.cs`, using a small tree fixture (reuse across tests in this
file): `Root` (no parent) → `Child` (parent = `Root`) → `Grandchild` (parent = `Child`), plus an
unrelated `Sibling` (no parent, not an ancestor of any of the three). Mock
`_objectives.GetByIdForTenantAsync` to return the right `Objective` per id.

- `IsEffectiveManagerAsync_SelfOwner_ReturnsTrue` — `Grandchild.OwnerId == employeeId` → `true`.
- `IsEffectiveManagerAsync_SelfActiveMember_ReturnsTrue` — `Grandchild.OwnerId != employeeId`, but
  `_members` mock returns an active `ProjectMember` row for `(Grandchild.Id, employeeId)` → `true`.
- `IsEffectiveManagerAsync_ParentOwner_ReturnsTrue` — checking `Grandchild.Id`; `Child.OwnerId ==
  employeeId`, `Grandchild.OwnerId` is someone else, no membership row anywhere → `true` (cascade from
  one level up).
- `IsEffectiveManagerAsync_GrandparentActiveMember_ReturnsTrue` — checking `Grandchild.Id`; `Root`
  has an active `ProjectMember` row for `employeeId` (not owner), `Child`/`Grandchild` have neither →
  `true` (cascade from two levels up, via membership not ownership).
- `IsEffectiveManagerAsync_SiblingOwner_ReturnsFalse` — checking `Grandchild.Id`; `employeeId` owns
  `Sibling` (unrelated branch, not an ancestor) → `false`. This is the regression guard against
  accidentally implementing a same-Project-wide check instead of an ancestor-chain check.
- `IsEffectiveManagerAsync_NoRelationship_ReturnsFalse` — `employeeId` has no owner/member row
  anywhere in the tree → `false`.

## Definition of done

- All 4 tasks committed (one commit for the interface+implementation change together, one for the test
  file changes — or combine into a single commit if that reads more naturally; this Part is small enough
  either way).
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~MilestoneMembershipCoordinator` green,
  6 new tests passing alongside the existing ones.
- `dotnet build` compiles clean (confirms nothing else implements `IMilestoneMembershipCoordinator` and
  now needs the new method too — `grep -rln "IMilestoneMembershipCoordinator" src/` to check for other
  implementers before assuming `MilestoneMembershipCoordinator` is the only one).
