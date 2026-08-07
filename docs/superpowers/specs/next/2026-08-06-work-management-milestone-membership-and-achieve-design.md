
# Work Management — Milestone Membership, Scoped Visibility, and Achieve Workflow — Design

**Status:** Approved by user 2026-08-06, ready for implementation planning.

**Builds on:** `docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md` (the tree-authorization model, Head/ReportingManager, and the request/approval workflow — all shipped and executed via `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`). This design does not change that plan's shipped tree-authorization rule (`OwnerId` = current Head has free control; creator never needs approval on their own creation) — it closes gaps found once the shipped feature was checked against the intended real-world user flow, and adds one new capability (Achieve) on top of it.

**Origin:** brainstormed live with the user 2026-08-06 via `superpowers:brainstorming`, triggered by a final-review gap list (three Important findings parked, not fixed, at the end of the milestone-hierarchy plan) plus the user's own end-to-end flow walkthrough of the whole Work Management module.

---

## 1. Goal

Close three gaps the shipped milestone-hierarchy feature left open, and add one new capability:

1. **Membership sync** — assigning someone as a milestone Head (or member) must actually grant them the tenant/employee validation and project-level membership row that visibility and `/projects/mine` depend on. Today it doesn't: an assigned Head gets `403` on the tree endpoint and never appears in their own `/projects/mine` list.
2. **Scoped visibility** — a milestone-level membership must only unlock that milestone's own subtree (plus ancestor context), never the whole project tree or other milestones' write endpoints. Today, `GetObjectiveTree` returns everything in the project to any member.
3. **Dynamic Reporting Manager** — a milestone's Reporting Manager (who approval requests route to) must track the parent milestone's *current* Head, not freeze at creation time. Today `ReportingManagerId` is written once at creation and never updated by Transfer.
4. **Achieve** — a new completion state for both Projects and Objectives, gated by "all direct children must already be Achieved," using the same request/approval mechanics already built for Delete.

## 2. Schema additions

**`objectives` and `projects` — one new column each:**
- `is_achieved boolean not null default false`
- `achieved_at timestamptz null`

No new "achieved by" column — `DecidedById`/`CreatedById` on the existing `ObjectiveChangeRequest` row (for the approval path) or the direct caller (for the immediate path) already capture this; recording it a second time on the entity itself would be redundant.

**`objective_change_requests` — two new `request_type` values**, reusing the existing table/workflow entirely: `achieve`, `unachieve`. No new table, no new columns — `PayloadJson` stays `null` for both (same as `delete`), since there's no proposed field values to carry, only a state transition.

**No project-level change-request table.** Project Achieve is Lead-only and always immediate (see §6) — the Project is the tree's root and, per the already-shipped rule, has no Reporting Manager to route a request to (identical reasoning to why Project Edit/Delete never create change requests).

**`project_members` — no schema change.** It already has `ObjectiveId` (added in Foundation, previously unused for non-Default objectives) and `IsActive`/`RemovedAt`. This design is the first thing to actually populate and rely on objective-scoped rows.

**`user_permission_overrides` — no schema change.** The table and entity (`UserPermissionOverride`: `UserId`, `PermissionId`, `GrantType` "grant"|"revoke", `Reason`, `GrantedBy`) already exist and are already read by `PermissionResolver`. Only a write path is missing (`IUserPermissionOverrideRepository` currently has just `ListForUserAsync`) — this design adds `AddAsync`.

## 3. Membership model

A `project_members` row's `ObjectiveId` defines its scope:

- **Direct membership** — `ObjectiveId == the Project's Default Objective`. This is what the project creator/lead already gets today (`MembershipSource = "system"`). A direct member sees the whole project tree and all project-level metadata, same as today.
- **Milestone-scoped membership** — `ObjectiveId == some non-default milestone`. Grants visibility into that milestone's own subtree only (§5), plus basic project metadata via `GetProjectById` (any active membership anywhere in the project still grants that — unchanged). `MembershipSource = "objective_invitation"` (the existing enum value, previously unused).

**Validation on every Head/member assignment** (Create's `headUserId` or default-to-creator, member-add, Transfer applying — never on request submission for the approval path):
1. `IEmployeeRepository.GetByUserIdAsync(tenantId, userId)` — not found → `400`.
2. `employee.EmploymentStatusId == EmploymentStatusIds.Active` (`1`, the seeded "active" code — see `LookupDataSeeder.cs`) — not active → `400`. New `EmploymentStatusIds` static class, `src/ONEVO.Domain/Lookups/EmploymentStatusIds.cs`, following the existing `VersionStatusIds` precedent (`src/ONEVO.Domain/Features/WorkManagement/Versions/Entities/VersionStatus.cs`) rather than a magic number at every call site.

**Create** (`POST /objectives`): after the existing authorization/conflict checks, resolve the effective Head (`headUserId ?? callerId`), validate them, create/reactivate their milestone-scoped membership. If the request also carries an initial member list, validate and add each the same way. All in the same transaction as the Objective insert.

**Add/remove milestone member** (new, §6 #9/#10): Head-only (same authorization as Create-a-child — "milestone creation permission" *is* being Head, not a separate delegable grant, confirmed with the user). Add validates + upserts a milestone-scoped membership; remove deactivates it (`IsActive = false`, `RemovedAt = now`) — it never deletes the row (audit trail, matching every other soft-delete convention in this codebase).

**Transfer** (`POST /objectives/{id}/transfer`, applying immediately or via approval — never on request submission):
1. Validate new Head (tenant + active).
2. Reassign `OwnerId` (unchanged from the shipped behavior).
3. Cascade-update `ReportingManagerId` on the milestone's *direct* children (one level — see §4) to the new Head.
4. Create/reactivate the new Head's milestone-scoped membership.
5. Deactivate the old Head's membership row for *this* milestone specifically.
6. Check whether the old Head has any other active milestone membership in this project, or a direct membership. If yes, they keep whatever access those grant. If no, deactivate their project participation entirely (there is exactly one row to deactivate in that case — the one just handled in step 5, since by definition they had no others).
7. All of steps 2–6 (plus the underlying `Update`/`SaveChangesAsync`) run in one transaction — any failure rolls back everything, including the `OwnerId` reassignment.

Steps 1–6 do not run when a Transfer request is merely *submitted* (creating a pending `objective_change_requests` row) — only when it *applies*: either immediately (creator transferring their own creation) or when `ApproveObjectiveChangeRequestCommandHandler` processes an approved `transfer` request. A rejected request leaves every membership row untouched.

**Achieve applying** (§6) runs the same "does this person still have another reason to be here" check as Transfer step 6, for the outgoing Head, once the milestone is frozen.

## 4. Dynamic Reporting Manager

`ReportingManagerId`'s meaning changes from "frozen at creation" to **"the parent milestone's current Head."** This requires no change to `CreateObjectiveCommandHandler`: the existing authorization rule already guarantees `caller == parent.OwnerId` at creation time (only a parent's current Head may create a child under it), so `ReportingManagerId = userId` at creation is *already* identical to `ReportingManagerId = parent.OwnerId` at that same moment — the two definitions only diverge later, when the parent's headship moves.

**The only new logic:** when a Transfer applies (§3 step 3), cascade:

```sql
UPDATE objectives
SET reporting_manager_id = @newHeadUserId, updated_at = @now
WHERE tenant_id = @tenantId AND parent_objective_id = @transferredObjectiveId AND is_active = true
```

One level only — grandchildren's Reporting Manager is their own direct parent, which didn't change, so they're correctly left alone. No recursive walk needed.

The Default Objective's own `OwnerId` (= the Project's `LeadId`) is still never transferred (unchanged, existing carve-out) — so children directly under the Default Objective keep a fixed Reporting Manager (the Lead) exactly as today, with no new cascade trigger, since nothing ever changes the Default Objective's headship.

Edit/Delete/Approve/Reject/`ListMyObjectiveChangeRequests` need no changes for this section — they already just read the stored column, which the cascade above keeps correct.

## 5. Scoped visibility (read side)

**`GetObjectiveTreeQueryHandler`** (`GET /projects/{projectId}/objectives`): authorization is unchanged (active membership somewhere in the project). The *query* changes:
- Direct member → unchanged: every active Objective in the project (today's behavior).
- Milestone-scoped member → their milestone's ancestor chain (walking `ParentObjectiveId` up to the Default Objective, for context — no sibling branches at any level) **plus** the full active descendant subtree rooted at their milestone.

A member with more than one milestone-scoped membership in the same project (e.g., Head of two separate milestones) sees the union of both subtrees plus both ancestor chains.

**New `GET /objectives/{id}`** — single milestone detail. Does not exist today (only `GetObjectiveTree`, which returns the whole reachable set, and Edit's response, which only fires on a write). Authorization: same permission-or-membership shape as `GetProjectById` — `projects:read`/`*` OR an active membership on this specific objective OR an active membership on any of its ancestors (so ancestor-context visibility from the tree endpoint is consistent with what a direct fetch-by-id allows too) OR a direct project membership.

**New `GET /objectives/mine/history`** — milestones the caller used to have active access to (via Head or membership) but no longer does, because they were Transferred away, removed as a member, or the milestone was Achieved and they had no other reason to stay in the project. Read-only; no write actions are exposed from this view. Sourced from `project_members` rows where `IsActive = false` and `RemovedAt` is set, joined to the Objective, filtered to the caller.

**`/projects/mine` and `GetProjectById`** — unchanged. Any active membership (direct or milestone-scoped) still grants both, exactly as shipped.

## 6. Achieve

**Fields:** `IsAchieved bool`, `AchievedAt DateTimeOffset?` on both `Objective` and `Project`.

**Precondition (both levels):** every direct child must already be `IsAchieved = true`. For a leaf (no children), the precondition is vacuously satisfied. This is deliberately shallow (direct children only) — since a parent can't achieve until its children have, and children can't achieve until *their* children have, the rule is transitively enforced bottom-up without needing a recursive check at any single call site.

**Authorization — Objective-level** (`POST /objectives/{id}/achieve`): identical shape to Delete (§4 of the shipped design) — caller must be the milestone's current Head; Default Objective excluded (`400`, same carve-out as Edit/Delete/Transfer); if caller is also the creator, applies immediately; otherwise creates a `pending` `achieve` change request routed to `ReportingManagerId`. At-most-one-pending and already-decided-is-409 both apply, unchanged from the existing convention.

**Authorization — Project-level** (`POST /projects/{id}/achieve`): Lead-only, always immediate — no approval path, matching the already-shipped root-of-tree exception (`DeleteProjectCommandHandler`/`EditProjectCommandHandler`). Precondition checks the Default Objective's direct children.

**Effect when applied (either level):** set `IsAchieved = true`, `AchievedAt = now`. The node becomes **frozen** — Edit, Transfer, and member add/remove all start returning `400`/`409` on an Achieved node, the same way they already do for a soft-deleted (`!IsActive`) one (§ of the final-review fix wave already shipped this exact pattern for `IsActive`; Achieved gets the identical treatment). Delete is untouched by this — an Achieved milestone can still be soft-deleted later if genuinely needed, matching how "frozen" only blocks *editing*, not removal.

Freezing also runs the outgoing-access check from §3 Transfer step 6 for the milestone's Head: if they have no other active milestone or direct membership in this project after the freeze, their project participation deactivates and the milestone becomes reachable only via `GET /objectives/mine/history`.

**Un-achieve** (`POST /objectives/{id}/unachieve`, `POST /projects/{id}/achieve`... — mirrors Achieve's own authorization exactly): clears `IsAchieved`/`AchievedAt`, unfreezes the node. No precondition (you can always revert). Reuses the `unachieve` request type from §2 for the approval path, same as Achieve does for `achieve`.

**`ApproveObjectiveChangeRequestCommandHandler`** gains two more `switch` arms (`achieve`, `unachieve`) alongside the existing `delete`/`edit`/`transfer` ones, each applying the effect described above. `RejectObjectiveChangeRequestCommandHandler` needs no changes — rejecting any request type already just flips the request's own status and leaves the Objective untouched.

## 7. Auto-grant `projects:access`

When a Head assignment resolves (Create, member-add doesn't grant this — only Head does, per the user's explicit confirmation that "milestone creation permission" is Head-only), check whether the assignee's effective permission set (`IPermissionResolver.ResolveAsync`) already includes `projects:access` or `*`. If not, insert a `UserPermissionOverride` row (`GrantType = "grant"`, `PermissionId` = `projects:access`'s id, `Reason` = a fixed string like `"Auto-granted on milestone head assignment"`, `GrantedBy` = the caller).

**Known limitation, stated plainly rather than silently accepted:** `RequirePermissionAttribute` (which gates `Create`/`Edit`/`Delete`/`ListMine`/`ListMyChangeRequests`/etc.) reads `ICurrentUser.Permissions`, which is sourced from claims baked into the session at login (`CurrentUserService.cs`) — not a live call to `IPermissionResolver`. A freshly-granted override will not let that user actually call any `projects:access`-gated endpoint until they log out and back in (or their session is otherwise refreshed). This is the same class of gap already surfaced and explicitly accepted, undocumented-fix, in the milestone-hierarchy plan's final review (finding "stale session claims across the rename") — this design does not attempt to solve session refresh, only to make the grant itself correct and auditable.

## 8. Endpoints — summary of changes

| # | Method + Route | Change |
|---|---|---|
| 1 | `POST /api/v1/work/objectives` | Modified — membership sync + employee validation + auto-grant for the resolved Head; optional initial member list |
| 2 | `POST /api/v1/work/objectives/{id}/transfer` | Modified — membership sync, RM cascade to direct children, auto-grant for new Head, outgoing-access check for old Head (apply-time only) |
| 3 | `POST /api/v1/work/objectives/{id}/members` | New — add a member (Head-only, employee-validated) |
| 4 | `DELETE /api/v1/work/objectives/{id}/members/{userId}` | New — remove a member (Head-only, deactivates the membership row) |
| 5 | `GET /api/v1/work/projects/{projectId}/objectives` | Modified — subtree-scoped for milestone-only members |
| 6 | `GET /api/v1/work/objectives/{id}` | New — single milestone detail |
| 7 | `GET /api/v1/work/objectives/mine/history` | New — read-only, past participation |
| 8 | `POST /api/v1/work/objectives/{id}/achieve` | New — same auth shape as Delete |
| 9 | `POST /api/v1/work/objectives/{id}/unachieve` | New — same auth shape as Achieve |
| 10 | `POST /api/v1/work/projects/{id}/achieve` | New — Lead-only, immediate |
| 11 | `POST /api/v1/work/projects/{id}/unachieve` | New — Lead-only, immediate |

Every other already-shipped endpoint (Edit/Delete Objective, Approve/Reject, List-mine-change-requests, all 5 Project endpoints) is unchanged in its own right, except for the new `!IsAchieved` freeze check added to Edit/Transfer/member-management (§6) alongside their existing `!IsActive` check.

## 9. Out of scope for this design

- Solving stale session claims / forced re-authentication on permission grant (§7) — flagged, not solved.
- A general-purpose, role-independent "delegable milestone creation permission" — confirmed with the user this is unnecessary; being Head already is the permission.
- Notification delivery for any of the new state transitions (Achieve, member add/remove) — same explicit deferral the original milestone-hierarchy design already made for change-request notifications.
- The two `next-plan/Project Management.md` items (project-level workflow status, approval pipeline, archive/restore, progress calculation) — this design's Project-level Achieve is a narrow, single-flag addition and does not attempt to unify with that broader, still-unbrainstormed feature set.

## 10. Self-review

- No placeholders — every rule traces to an explicit answer from the 2026-08-06 brainstorming session, including the ones that reversed an earlier answer mid-session (milestone member management, dynamic Reporting Manager) — both re-confirmed once the reversal was made explicit to the user.
- Internally consistent with the shipped milestone-hierarchy design: the tree-authorization rule (§4 of that design) is unchanged; this design only adds membership/visibility side-effects and one new state (Achieve) on top of it, verified against actual shipped code (`CreateObjectiveCommandHandler`, `TransferObjectiveHeadCommandHandler`, `ApproveObjectiveChangeRequestCommandHandler`, `PermissionResolver`, `CurrentUserService`, `ProjectMember`/`Employee`/`UserPermissionOverride` entities) rather than assumed from memory.
- Scope: this is one coherent vertical slice (membership lifecycle + scoped visibility + Achieve), not decomposable into independent pieces — the Achieve freeze depends on the same `!IsActive`-style guard pattern the membership work also touches, and Achieve's membership cleanup reuses Transfer's outgoing-access check directly.
- Ambiguity resolved: every point where the user's dictated rules were genuinely ambiguous (auto-grant vs. restate-existing-rule; Achieve-replaces-Delete vs. Achieve-is-separate; ancestor visibility in the scoped tree; what "direct membership" means; ReportingManagerId reassignment) was put to the user directly via `AskUserQuestion` rather than guessed, including the two answers that were explicitly walked back and re-confirmed once the reversal was surfaced.
