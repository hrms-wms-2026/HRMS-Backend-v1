# Work Management — Cascading Objective Ownership (Design)

**Status:** Approved 2026-08-21 (chat brainstorm with user, backend repo only — frontend needs no
changes, it already renders whatever `IsOwner` the tree API returns).

## 1. Problem

Every write action under an Objective (Module) — create a sub-module, edit a module, add/remove a
member, create/edit/achieve/complete a Sprint, create/delete a Task, move a Task into a `private`
status — is gated by the same repeated check:

```csharp
if (objective.OwnerId != callerEmployeeId.Value)
    return Result<T>.Forbidden("Only this milestone's owner/head can ...");
```

This is a **direct-match-only** check against the single `Objective.OwnerId` field. It does not
consider:
- Whether the caller is an active `ProjectMember` of that exact Objective (a non-owner member today
  can never create things directly — they submit a `TaskCreationRequest`/`TaskEditRequest` for the
  owner to approve instead).
- Whether the caller owns or is a member of an **ancestor** Objective. Managing a top-level Module
  today requires being separately added as owner/member of every descendant Module one at a time.

Confirmed with the user (2026-08-21 chat): rights should **cascade down** the tree — anyone who owns
or is an active member of an Objective should get full rights on that Objective and **every
descendant of it, at any depth**. Rights never flow up: a descendant's owner/member gets zero extra
rights on ancestors (already correct today — no change needed there).

## 2. Scope

**Cascade grant:** for Objective `D`, the caller is an *effective manager* of `D` if they are the
`OwnerId` of `D`, **or** an active `ProjectMember` of `D`, **or** the `OwnerId`/an active
`ProjectMember` of any ancestor of `D` (parent, grandparent, ... up to the Project's default
Objective). This replaces the `objective.OwnerId != callerEmployeeId.Value` check everywhere it
appears.

Confirmed explicitly: this is **Owner + regular members**, not owner-only. A plain (non-owner) member
of a parent Module gets full direct create/edit/delete rights on every descendant — this intentionally
bypasses the `TaskCreationRequest`/`TaskEditRequest` approval workflow *for that cascaded relationship*.
The approval-request workflow is unchanged for a **non-owner member acting on their own exact-node**
Objective — that still requires a request, only the ancestor-cascade path grants direct rights.

**Confirmed actual behavior (2026-08-24, explicit user decision after Final Review flagged this as a
possible defect):** because every Project has exactly one root Objective (the default Objective,
`ParentObjectiveId == null`, created by `CreateProjectCommandHandler`) and every accepted Project
member's `ProjectMember` row is scoped to that same default Objective, the ancestor walk in §3 always
reaches a node — the default Objective — where every Project member passes the "active `ProjectMember`"
branch. **In practice this means any accepted Project member is an effective manager of every Objective,
Sprint, and Task in that Project**, not only of Objectives below a Module they specifically belong to.
This is intended, not a bug — confirmed explicitly with the user. One consequence: the
`TaskCreationRequest`/`TaskEditRequest` approval workflow and the `MoveTaskStatus`
`TaskStatusVisibilities.Private` gate (§4) retain real effect only for a caller who is **not** a Project
member at all (no `ProjectMember` row on the default Objective or any ancestor) — not, as the paragraph
above might otherwise be read, "any non-owner member of the same Project."

**Explicitly out of scope (unchanged):**
- Read-side reachability in `GetObjectiveTreeQueryHandler`, `GetObjectiveSprintsQueryHandler`,
  `GetSprintTasksQueryHandler` — these already resolve a superset (ancestors *and* descendants of any
  owned/member node) for visibility, and that's correct and stays as-is. Only the tree's `IsOwner`
  **display flag** needs updating for consistency (§4).
- The `projects:read`/`*` tenant-permission bypass in the two query handlers above — confirmed with
  the user to keep as-is, no change.
- Task Foundation's request/approval commands themselves
  (`CreateTaskCreationRequest`/`ApproveTaskCreationRequest`/etc.) — unchanged; they remain the path for
  a non-cascaded, non-owner member on their own exact Objective.

## 3. New shared authorization method

Add to `IMilestoneMembershipCoordinator` (`src/ONEVO.Application/Features/WorkManagement/Objectives/Services/`):

```csharp
Task<bool> IsEffectiveManagerAsync(Guid tenantId, Guid objectiveId, Guid employeeId, CancellationToken ct = default);
```

Implementation walks the Objective itself, then its ancestor chain via `ParentObjectiveId` (same
pattern already used inline in `GetObjectiveSprintsQueryHandler`), returning `true` on the first node
where `OwnerId == employeeId` or an active `ProjectMember` row exists for that node. Requires injecting
`IObjectiveRepository` into `MilestoneMembershipCoordinator` (not currently a dependency).

## 4. Call sites to update

Replace `objective.OwnerId != callerEmployeeId.Value` with
`!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct)` in:

- `Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs` (checks `parent.OwnerId`)
- `Objectives/Commands/EditObjective/EditObjectiveCommandHandler.cs`
- `Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
- `Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs`
- `Sprints/Commands/CreateSprint/CreateSprintCommandHandler.cs`
- `Sprints/Commands/EditSprint/EditSprintCommandHandler.cs`
- `Sprints/Commands/AchieveSprint/AchieveSprintCommandHandler.cs`
- `Sprints/Commands/CompleteSprint/CompleteSprintCommandHandler.cs`
- `Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs`
- `Tasks/Commands/DeleteTask/DeleteTaskCommandHandler.cs`
- `Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs` — this one is a two-step check
  today (owner bypass, else must be a member AND status must not be `private`). New shape: if
  `IsEffectiveManagerAsync` is true, allow any status move (this is the "private status accessible to
  Objective owner and all Parent members" rule); otherwise keep today's member-check +
  public-status-only gate for the caller's own exact-node non-cascaded membership.

Each of the above gets one commit, matching this repo's usual one-task-one-commit process. Run
`grep -rn "objective.OwnerId != callerEmployeeId" src/ONEVO.Application/Features/WorkManagement/` before
and after to confirm every call site was found and updated — don't rely on the list above alone in
case another one was missed.

**Not changed:** `AssignTask`/`UnassignTask`/`ApproveTaskCreationRequest` etc. were not found to use
this exact `objective.OwnerId != callerEmployeeId.Value` pattern during this design's research — verify
with the same grep above; if any of them do turn out to use it, they get the same treatment, but they
were not in the list of files the grep matched at design time.

## 5. Tree `IsOwner` display flag

`GetObjectiveTreeQueryHandler` currently sets `IsOwner = ownedObjectiveIds.Contains(o.Id)` where
`ownedObjectiveIds` is the set of Objectives the caller has an active `ProjectMember` row on
(**direct match only** — despite the name, this checks membership, not the `OwnerId` field). This
must become "does the caller have an active membership on this Objective *or any ancestor*."

Cheapest correct implementation: the handler already computes a `reachable` set via BFS both up
(ancestors) and down (descendants) from each directly-owned Objective (see the existing
`while (cursor.ParentObjectiveId is not null ...)` and `queue`-based descendant walk in the non-default-
member branch). Build a **second**, descendant-only set — for each directly-owned Objective, BFS
*down* only (skip the ancestor walk) — and set `IsOwner = ownedObjectiveIds.Contains(o.Id) ||
ownerReachable.Contains(o.Id)`. Do this in **both** branches of the handler (the `hasDirectMembership`
early-return branch too — a default-Objective member who also happens to directly own a non-default
Objective elsewhere in the tree still needs the same cascade applied to that Objective's descendants).

No response-shape change — `IsOwner` is already a `bool` field on `ObjectiveTreeItemResponse`
(added by the already-shipped Part 3 of the tree/sprint/task unified view work). Frontend needs no
changes; `MilestoneTreeTabComponent`'s row components already gate icons on this flag.

## 6. Testing

Unit-test `IsEffectiveManagerAsync` directly: self-owner, self-member, parent-owner,
grandparent-member, sibling-owner (must be `false` — cascade is down the caller's own ancestor chain
only, not sideways), and no-relationship (`false`). Then one test per updated command handler
confirming a grandparent-level member can now perform the action, and confirming an unrelated user
(or a sibling-branch owner) still cannot. For `MoveTaskStatus`, add the specific case from this
session's discussion: a parent-Objective member (not the exact Objective's owner) can move a task into
a `private`-visibility status on the child.

## 7. Out of scope for this design

Task Status/Category moving from per-Objective to per-Project scope, and the tree-tab UI bug fixes
(status icon/text overlap, missing Task delete icon, Module filter) are separate, independent pieces —
see the companion design doc and the bounded bug-fix plan, not covered here.
