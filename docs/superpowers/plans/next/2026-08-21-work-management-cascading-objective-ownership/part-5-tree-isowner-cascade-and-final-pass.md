# Part 5: Cascade the tree's `IsOwner` display flag + full-feature regression pass

**Read first:** `docs/superpowers/specs/next/2026-08-21-work-management-cascading-objective-ownership-design.md`
§5. Do this Part **last**, after Parts 1-4 are all done and green — it's the one place this design
touches read-side code, and its own "full regression" task doubles as the final check for the whole
feature.

**Scope guard:** Work Management module only.

**Status:** shipped 2026-08-22. Tasks 1-3: `GetObjectiveTreeQueryHandler`'s `IsOwner` display flag now
cascades to descendants of any Objective the caller effectively manages, in both response branches. Task
4's full-module regression grep caught one more handler outside every prior Part's literal scope
(`RequestAllocationExtensionCommandHandler`) — converted under the same self-correcting-clause authority,
full review+fix-loop cycle. All 5 Parts of this plan are now code-complete; 18 Postman docs updated for
the new cascaded authorization wording. **Stays in `next/`, not `finished/`** — no frontend code changes
in this design, but a manual browser pass confirming the tree UI actually shows cascaded action icons for
a non-owner ancestor-member is still outstanding.

## Goal

`GetObjectiveTreeQueryHandler` sets `IsOwner = ownedObjectiveIds.Contains(o.Id)` — direct membership on
the exact node only, despite the tree already fetching a wider reachable set (ancestors + descendants).
Frontend action icons (already shipped, `MilestoneTreeTabComponent`'s row components) gate on this flag,
so this is the one change needed for the UI to actually show cascaded rights — no frontend code changes
in this Part.

## Current state (verified — this is the full current `Handle` method, `GetObjectiveTreeQueryHandler.cs`)

```csharp
public async Task<Result<IReadOnlyList<ObjectiveTreeItemResponse>>> Handle(GetObjectiveTreeQuery request, CancellationToken ct)
{
    if (!_currentUser.IsAuthenticated)
        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("Authentication required.");

    var tenantId = _currentUser.TenantId;
    var userId = _currentUser.UserId;
    if (tenantId == Guid.Empty)
        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("Tenant context missing.");

    var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
    if (callerEmployeeId is null)
        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("No employee record for the current user.");

    var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
    if (project is null || !project.IsActive)
        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.NotFound("Project not found.");

    var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
    if (!isMember)
        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("You do not have access to this project's milestone tree.");

    var allObjectives = await _objectives.GetTreeByProjectIdAsync(tenantId, project.Id, ct);
    var ownedObjectiveIds = (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct)).ToHashSet();

    var defaultObjective = allObjectives.FirstOrDefault(o => o.IsDefault);
    var hasDirectMembership = defaultObjective is not null
        && await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, project.Id, callerEmployeeId.Value, new[] { defaultObjective.Id }, ct);

    if (hasDirectMembership)
    {
        var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, allObjectives.Select(o => o.OwnerId).Distinct().ToList(), ct);
        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Success(
            allObjectives.Select(o => ObjectiveMapper.ToTreeItem(o, ownedObjectiveIds.Contains(o.Id), namesByEmployeeId.GetValueOrDefault(o.OwnerId))).ToList());
    }

    var byId = allObjectives.ToDictionary(o => o.Id);
    var childrenByParent = allObjectives
        .Where(o => o.ParentObjectiveId is not null)
        .GroupBy(o => o.ParentObjectiveId!.Value)
        .ToDictionary(g => g.Key, g => g.ToList());

    var reachable = new HashSet<Guid>();
    foreach (var ownedId in ownedObjectiveIds)
    {
        if (!byId.TryGetValue(ownedId, out var owned))
            continue;

        reachable.Add(owned.Id);

        var cursor = owned;
        while (cursor.ParentObjectiveId is not null && byId.TryGetValue(cursor.ParentObjectiveId.Value, out var parent))
        {
            reachable.Add(parent.Id);
            cursor = parent;
        }

        var queue = new Queue<Guid>();
        queue.Enqueue(owned.Id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
                continue;

            foreach (var child in children)
            {
                if (reachable.Add(child.Id))
                    queue.Enqueue(child.Id);
            }
        }
    }

    var scopedObjectives = allObjectives.Where(o => reachable.Contains(o.Id)).ToList();
    var scopedNamesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
        tenantId, scopedObjectives.Select(o => o.OwnerId).Distinct().ToList(), ct);
    var scoped = scopedObjectives
        .Select(o => ObjectiveMapper.ToTreeItem(o, ownedObjectiveIds.Contains(o.Id), scopedNamesByEmployeeId.GetValueOrDefault(o.OwnerId)))
        .ToList();
    return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Success(scoped);
}
```

Note: `ownedObjectiveIds` here comes from `GetActiveObjectiveIdsForEmployeeInProjectAsync` — active
`ProjectMember` rows only, **not** `Objective.OwnerId`. A caller who is the `OwnerId` of an Objective but
was never separately added as its `ProjectMember` would currently get `IsOwner: false` on their own
node — check whether that's actually possible in this codebase (e.g. does creating a sub-module also
`UpsertMembershipAsync` the creator?) before assuming it's a real gap; if it turns out `OwnerId` and
membership are always kept in sync elsewhere, no extra fix is needed here beyond the cascade itself.

## Files to modify

- `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs`
- Matching test file — `grep -rln "GetObjectiveTreeQueryHandlerTests" tests/`.

## Task 1: Build a shared `childrenByParent` + a descendant-only cascade set, once, before both branches

Move the `byId`/`childrenByParent` construction (currently only built in the non-default-member branch)
to run unconditionally, right after `allObjectives`/`ownedObjectiveIds` are computed — both branches need
it now. Then add one new block, also unconditional, that computes descendant-only cascade (no ancestor
walk — rights flow down only):

```csharp
var byId = allObjectives.ToDictionary(o => o.Id);
var childrenByParent = allObjectives
    .Where(o => o.ParentObjectiveId is not null)
    .GroupBy(o => o.ParentObjectiveId!.Value)
    .ToDictionary(g => g.Key, g => g.ToList());

var ownerReachable = new HashSet<Guid>();
foreach (var ownedId in ownedObjectiveIds)
{
    if (!byId.ContainsKey(ownedId))
        continue;

    var queue = new Queue<Guid>();
    queue.Enqueue(ownedId);
    while (queue.Count > 0)
    {
        var current = queue.Dequeue();
        if (ownerReachable.Add(current) && childrenByParent.TryGetValue(current, out var children))
        {
            foreach (var child in children)
                queue.Enqueue(child.Id);
        }
    }
}
```

## Task 2: Use `ownerReachable` in both branches' `IsOwner` computation

In the `hasDirectMembership` branch, change:
```csharp
allObjectives.Select(o => ObjectiveMapper.ToTreeItem(o, ownedObjectiveIds.Contains(o.Id), namesByEmployeeId.GetValueOrDefault(o.OwnerId))).ToList());
```
to:
```csharp
allObjectives.Select(o => ObjectiveMapper.ToTreeItem(o, ownedObjectiveIds.Contains(o.Id) || ownerReachable.Contains(o.Id), namesByEmployeeId.GetValueOrDefault(o.OwnerId))).ToList());
```

In the non-default-member branch, remove the now-duplicate `byId`/`childrenByParent` declarations (moved
to Task 1) but keep the existing `reachable` (ancestors+descendants, for tree *visibility*, unchanged —
do not confuse this with the new `ownerReachable`, they serve different purposes: `reachable` decides
what's in the response at all, `ownerReachable` decides the `IsOwner` flag on what's already included).
Change:
```csharp
.Select(o => ObjectiveMapper.ToTreeItem(o, ownedObjectiveIds.Contains(o.Id), scopedNamesByEmployeeId.GetValueOrDefault(o.OwnerId)))
```
to:
```csharp
.Select(o => ObjectiveMapper.ToTreeItem(o, ownedObjectiveIds.Contains(o.Id) || ownerReachable.Contains(o.Id), scopedNamesByEmployeeId.GetValueOrDefault(o.OwnerId)))
```

## Task 3: Tests

Add a test with a 3-level tree (`Root` → `Child` → `Grandchild`) where the caller is an active member of
`Child` only (not `Root`, not `Grandchild`, not the Project's default Objective — so this hits the
non-default-member branch): assert `Root.IsOwner == false` (ancestor, view-only — unchanged behavior),
`Child.IsOwner == true` (direct, unchanged), `Grandchild.IsOwner == true` (**new** — this is the cascade
this Part adds). Keep an existing "default-Objective member sees `IsOwner` only on their own directly-
owned node, not on unrelated Objectives elsewhere in the tree" test as a regression check for the
`hasDirectMembership` branch, then add the same 3-level cascade assertion for that branch too (a
default-Objective member who separately owns a non-default Objective elsewhere in the tree — that
Objective's descendants must also cascade).

## Task 4: Full-feature regression pass (all of Parts 1-5)

1. `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` — read the full output,
   not just the pass/fail count.
2. `dotnet build` on the full solution.
3. `grep -rn "objective.OwnerId != callerEmployeeId\|parent.OwnerId != callerEmployeeId"
   src/ONEVO.Application/Features/WorkManagement/` — should return nothing anywhere in the module now
   (confirms Parts 2-4 together covered every call site, not just the ones each Part's own research
   found).
4. Update `docs/postman-request/Work Management/` docs for any endpoint whose authorization description
   explicitly mentions "owner" in a way that's now inaccurate (e.g. "Only this milestone's owner can
   create sprints" in `Create Sprint.md` — the permission line should now say the rule cascades from any
   ancestor Objective's owner/member, not just the exact node's owner). Grep
   `grep -rln "milestone's owner\|milestone's head" "docs/postman-request/Work Management/"` to find every
   file that needs this wording update.
5. Update this plan folder's own status: once all 5 Parts are committed and the above all pass, add a
   `**Status:** shipped <date>` line to the top of each Part file (matching the convention in the
   `2026-08-20-work-management-tree-sprint-task-unified-view` plan folder), and update
   `docs/superpowers/plans/next/SUMMARY.md` / `docs/superpowers/plans/SUMMARY.md` accordingly. This
   feature stays in `next/` (not `finished/`) until a manual browser pass confirms the tree UI actually
   shows cascaded icons correctly end-to-end — this design has no frontend code changes, but the *result*
   is only visible by looking at the tree in a browser as a cascaded (non-owner, ancestor-member) user.

## Definition of done

- Tasks 1-3 committed together (one logical change to one handler) or split if that reads more naturally.
- Task 4's full regression pass is clean end to end.
- Postman docs updated per Task 4 step 4.
- Plan status updated per Task 4 step 5, but the plan folder itself stays in `next/` pending manual
  browser verification.
