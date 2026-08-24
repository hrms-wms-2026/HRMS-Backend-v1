# Part 3: Enrich `GetObjectiveTreeQuery`'s response with Progress, OwnerName, IsOwner

**Read first:** `docs/superpowers/specs/next/2026-08-20-work-management-tree-sprint-task-unified-view-design.md`
§4 (rewritten 2026-08-21 — the frontend Tree tab is switching its data source to this endpoint).

**Scope guard:** Work Management module only.

**Status:** shipped 2026-08-21 (Part 3 Tasks 1-5). `GetObjectiveTreeQuery` now returns `Progress`, `OwnerName`, and per-node `IsOwner` (direct membership on that exact Objective, not reachable-via-walk).

## Goal

The frontend Tree tab currently calls `GET /work/objectives/{id}/tree` (`GetObjectiveSubtreeQuery`,
single-node-rooted). It's switching to `GET /api/v1/work/projects/{projectId}/objectives`
(`GetObjectiveTreeQuery`, project-wide, ancestor-aware — already handles the "child sees parent's tree
context but not parent's own Sprints/Tasks" visibility rule correctly via its reachability walk) so a
project-wide tree with correct per-branch visibility can be rendered. See frontend Part 1 (written
separately, same feature) for the client-side consumer of this.

But `ObjectiveTreeItemResponse`/`ObjectiveTreeItemViewModel` (the DTOs this query returns) are missing
three fields the tree UI needs that `ObjectiveSubtreeNodeResponse` already has: `Progress`, a resolved
`OwnerName`, and — critically — an `IsOwner` flag. Without `IsOwner` there is no way for the frontend to
tell "this node is in my own branch, show the 6 action icons" apart from "this node is only visible as
ancestor context, render view-only" — which is an explicit, confirmed requirement (module rows outside the
caller's own branch must render with zero action icons, even though the wider tree is now visible).

## Current state (verified by reading the handler directly, not assumed)

`GetObjectiveTreeQueryHandler.cs` (`src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs`):
- Line 50-52: checks `HasActiveMembershipAsync` (any active membership in the project) — gates the whole
  call, not per-node.
- Line 56-61: **early-return branch** — if the caller has direct membership on the Default Objective, it
  returns `allObjectives.Select(ObjectiveMapper.ToTreeItem)` with **no per-node ownership computed at
  all**. This branch needs fixing too — today it silently returns every node with no way to tell which
  ones the caller actually owns.
- Line 63: `ownedObjectiveIds` (`IProjectMemberRepository.GetActiveObjectiveIdsForEmployeeInProjectAsync`)
  is only fetched in the **second** branch (non-default-member caller), used purely to compute the
  `reachable` HashSet for filtering — it is never attached per-node to the response either.
- `ObjectiveMapper.ToTreeItem(Objective objective)` (line 22-24 of `ObjectiveMapper.cs`) takes only the
  entity, no caller context, and does not set `Progress`, an owner name, or an ownership flag.
- `ObjectiveTreeItemResponse` (`.../DTOs/Responses/ObjectiveTreeItemResponse.cs`) fields today: `Id,
  ParentObjectiveId, IsDefault, Title, OwnerId, StartDate, EndDate, AllocatedHours, CompletedHours,
  IsActive, IsAchieved`. No `Progress`, no `OwnerName`, no `IsOwner`.
- `ObjectiveTreeItemViewModel` (`src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveTreeItemViewModel.cs`)
  mirrors the same fields 1:1 via `ObjectiveViewModelMapper.ToViewModel` — must be extended in lockstep or
  the new fields never reach the wire.
- Employee display-name resolution for a batch of ids already exists and is reused verbatim elsewhere in
  this feature area: `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync(tenantId, ids, ct)` (see
  `GetObjectiveSubtreeQueryHandler.cs` line 80, `GetObjectiveByIdQueryHandler.cs` line 76). Reuse this, do
  not write a new name-resolution path.
- `Objective.Progress` already exists on the domain entity and is already read by `ObjectiveMapper.ToDetail`
  / `ToSubtreeNode` — just wasn't threaded into `ToTreeItem`.

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveTreeItemResponse.cs`
- `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`
- `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs`
- `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveTreeItemViewModel.cs`
- `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/GetObjectiveTreeQueryHandlerTests.cs` (or
  wherever the existing tests for this handler live — find via
  `grep -rl GetObjectiveTreeQueryHandler tests/`)

## Tasks (small, do in order, one commit per task)

1. **Extend `ObjectiveTreeItemResponse`**: add `decimal Progress, string? OwnerName, bool IsOwner` to the
   record (append at the end, positional record — do not reorder existing params, every call site would
   break silently).

2. **Extend `ObjectiveMapper.ToTreeItem`**: change signature to
   `ToTreeItem(Objective objective, bool isOwner, string? ownerName = null)`, pass `objective.Progress`,
   `ownerName`, `isOwner` into the new record fields. Update the one call site inside
   `GetObjectiveTreeQueryHandler` (task 3) — grep first to confirm no other caller exists
   (`grep -rn "ObjectiveMapper.ToTreeItem" src/`), since this is a breaking signature change.

3. **Rework `GetObjectiveTreeQueryHandler`** so both branches compute per-node ownership and names:
   - Move `var ownedObjectiveIds = await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(...)`
     (currently line 63) **above** the `hasDirectMembership` branch (currently lines 56-61) so it's
     available to both paths. Materialize as `.ToHashSet()` for O(1) `Contains` checks.
   - Resolve names once: `var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
     tenantId, allObjectives.Select(o => o.OwnerId).Distinct().ToList(), ct);` — do this once for whichever
     objective set actually gets returned (either `allObjectives` or `scoped`, depending on branch) to
     avoid resolving names for objectives that get filtered out in the non-default-member branch.
   - Direct-membership branch: replace
     `allObjectives.Select(ObjectiveMapper.ToTreeItem)` with
     `allObjectives.Select(o => ObjectiveMapper.ToTreeItem(o, ownedObjectiveIds.Contains(o.Id),
     namesByEmployeeId.GetValueOrDefault(o.OwnerId)))`.
   - Non-default-member branch: same change applied to the existing `scoped` projection (currently line
     102), using the already-computed `ownedObjectiveIds`/`namesByEmployeeId`.
   - **`IsOwner` semantics, confirmed with the user**: `IsOwner` on a given node means "the caller has
     direct active membership on this exact Objective" (i.e. `ownedObjectiveIds.Contains(objective.Id)`) —
     NOT "this node is anywhere in the caller's reachable branch." A node reachable only because it's an
     ancestor of, or descendant of, a node the caller owns must have `IsOwner = false`. This is what lets
     the frontend render ancestor-context modules as view-only while still showing them in the tree. Do not
     conflate this with the `hasDirectMembership` project-wide boolean used for the early-return branch
     gate — that boolean answers a different question (does the caller own the Default Objective) and must
     not be reused as a per-node flag.

4. **Extend `ObjectiveTreeItemViewModel` + `ObjectiveViewModelMapper.ToViewModel`**: add the same three
   fields, mapped straight through from the `ObjectiveTreeItemResponse`. Without this the new fields never
   leave the Application layer.

5. **Tests** (extend the existing test file for this handler — do not create a parallel one):
   - Direct-member-of-Default-Objective caller: assert `IsOwner = true` on nodes they own, `false` on
     nodes owned by a different objective/employee within the same tree.
   - Non-default-member caller (owns a child objective, not Default): assert the returned list still
     includes the ancestor chain (existing behavior, don't break it) AND that the ancestor nodes have
     `IsOwner = false` while the caller's own owned node(s) have `IsOwner = true`.
   - Assert `Progress` and `OwnerName` are populated and match the source `Objective`/employee record for
     at least one node in each branch.
   - Assert `OwnerName` is `null` (not throwing) when the owner id isn't in the resolved names dictionary
     (defensive — mirrors `ResolveName`'s existing null-safety in `ObjectiveMapper`).

## Definition of done

- All 5 tasks committed individually.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green.
- Full solution `dotnet build` compiles clean (the `ToTreeItem` signature change is breaking — confirm no
  other caller was missed).
- `docs/postman-request/Work Management/Get Objective Tree.md` — check if this file already exists; if not,
  create it (6-section format) since this endpoint is about to become load-bearing for the frontend and
  currently has no doc. If it exists, update its response example with the 3 new fields.
