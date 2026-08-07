
# Work Management — Milestone (Objective) Hierarchy, Head/Reporting-Manager, and Approval Workflow — Design

**Status:** Approved by user 2026-08-04, ready for implementation planning.

**Builds on:** `docs/superpowers/plans/2026-08-03-work-management-foundation.md` (Slice 1 — Projects + a single auto-created Default Objective per project) and revises the permission model assumed by `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md` (Slice 2 — Project Edit/Delete/GetById/List, not yet executed as of this design).

**Supersedes:** the "Milestone (Objective) In-Charge Role & Permission System" raw-context section previously captured in `docs/superpowers/next-plan/Project Management.md` — that section is now trimmed to a pointer at this file. The "Project Lifecycle Workflow, Approval Pipeline, Archive/Restore, and Progress Calculation" section in the same file remains separately deferred and out of scope here (see §8).

**Origin:** brainstormed live with the user 2026-08-04 via `superpowers:brainstorming`, driven by a business-rule dictation covering the full Objective/Milestone tree, not just the permission-model piece the earlier raw-context note anticipated.

---

## 1. Goal

Turn Objectives ("Milestones" in frontend/user language) into a real hierarchical tree with exactly one Head per node, a fully hardcoded (non-RBAC) authorization model based on tree position, and a request/approval mechanism for the one class of actions a non-root Head cannot perform unilaterally on their own node. Collapse the Work Management permission surface from three codes (`projects:read`/`projects:write`/`projects:create`) — and the many more the original Foundation design anticipated adding per-feature (`members:manage`, `invitations:manage`, `versions:write`, `labels:manage`, etc.) — down to two: one module-wide access gate and one cross-user visibility grant. Everything else is tree position, not a permission lookup.

## 2. Permission model (module-wide, replaces the three-code model)

| Code | Grants |
|---|---|
| `projects:access` | Base Work Management module gate. Without it, a user cannot use any Work Management endpoint — including `GET /projects/mine`, which previously required no permission at all. With it, a user can create/view/edit their **own** projects and milestones (tree-based scope, see §4). Replaces `projects:create` and `projects:write` everywhere they are currently used. |
| `projects:read` | Unchanged. Cross-user visibility — the admin/company-owner path (`GET /projects?userId=`). Every other capability this design defines (creating a sub-milestone, editing/deleting/transferring a milestone) is gated by `projects:access` + tree position only, never by an additional permission code. |

**Immediate impact on already-existing work:**
- `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md` (Slice 2, not yet executed) is updated by this design to use `projects:access` in place of `projects:write` on `Edit`/`Delete`, and to add a `projects:access` requirement to `ListMine` (`/mine`), which previously required none.
- `PermissionSeeder.cs` and `ProjectsController.Create`'s `[RequirePermission("projects:create")]` (already shipped, Foundation Slice 1) are **not** touched by this design directly — that is a separate, explicitly-scoped follow-up (code change, not a docs change), tracked as an open item in §9.

## 3. Schema additions

No change to `project_members` — its existing "Forbidden: `role`" rule (`phase1-table-inventory.md`) stays intact. Head/reporting-manager are properties of the **Objective** itself, not of a membership row, since every Objective has exactly one Head at a time (not a per-member flag).

**No new "Head" column — `Objective.OwnerId` already is the Head.** `Objective` (Foundation Slice 1, `src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/Objective.cs`) already has `public Guid OwnerId { get; set; }`, and `CreateProjectCommandHandler` already sets `OwnerId = userId` (the creator) when constructing the Default Objective — that is exactly this design's "Head defaults to the creator" rule, already shipped under a different name. This design does not add a `HeadUserId` column; it defines **`OwnerId` as meaning Head** everywhere below, reassignable via Transfer (§6). No migration is needed for this part.

**`objectives` — one genuinely new column:**
- `reporting_manager_id uuid null` — the user who created this Objective (`CreatedById`, from `BaseEntity`, is already recorded — `ReportingManagerId` is a deliberate denormalization of it, not a new independent fact). Kept as its own column purely so the approval-queue query (`GET .../change-requests/mine`, §6) can filter directly without joining back through `CreatedById`'s ambiguous "created by" semantics on other entities. `null` only for the Default Objective of a Project's root (see §4 — the Project's own Lead is the tree's true root and has no reporting manager for approval purposes).

**New table `objective_change_requests`** — one row per pending Delete/Edit-conflict/Transfer request awaiting Reporting-Manager approval:
- `id`, `tenant_id`, `objective_id` (the Objective the request is about), `request_type` (`delete` | `edit` | `transfer`), `requested_by_id`, `reporting_manager_id` (resolved at request time, the approver), `status` (`pending` | `approved` | `rejected`), `payload_json` (the proposed new field values for `edit`/`transfer`; empty for `delete`), `decided_at`, `decided_by_id`, `created_at`.
- On approval: the underlying action (soft-delete / field update / `OwnerId` reassignment) is applied automatically in the same transaction as the approval — the requester does not take a second action.
- On rejection: the Objective is left unchanged; the request row is kept (status `rejected`) for history, not deleted.

## 4. The recursive tree-authorization rule (fully hardcoded — no permission-table lookup beyond the base `projects:access` gate)

For any Objective node `O`:

- **Free control follows current headship, not ancestry.** Whoever currently holds `O.OwnerId` (`H`) has free, unrestricted control (edit, delete, transfer, create-sub-milestone-under) over `O`. This is **not** transitive up the whole ancestor chain — it does not mean `H`'s own Reporting Manager, or anyone further up, can bypass `H` to act on `O` directly. Control over a subtree stays with whoever currently heads each node in it; it only moves to someone else via an explicit Transfer (§6 #4), at which point free control of that node (and, by the same rule applied again, everything currently headed by *that* person beneath it) passes to the new Head.
- **The one exception is creation:** a new child Objective always starts out headed by its creator by default (§5), so a Head's control naturally extends downward through everything they've created and not yet transferred away — this is why the worked example below reads as "control cascades down," without needing a separate ancestor-bypass rule.
- **Any of the three sensitive actions on `O` itself — delete, an edit that would conflict with `O`'s parent's constraints (deadline, allocated hours — the existing warning-only-today values from `phase1-table-inventory.md`), or transferring headship of `O`** — requires a request routed to `O.reporting_manager_id` (§3) for approval, **unless** the caller is also `O`'s own creator (`O.CreatedById`), who never needs approval for actions on something they created themselves. A **non-conflicting** edit to `O` (one that stays within the parent's constraints) applies immediately regardless of who's asking, no approval needed — only conflicting edits, deletes, and transfers go through the request/approval path, and only for a non-creator Head.
- **Exception — the tree's true root:** the Project's own Lead needs no approval for anything on the Project's Default Objective (or the Project itself). There is nothing above them to route a request to. This is the existing, already-designed `DeleteProjectCommandHandler` rule (`docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md` Task 4) restated as the tree's base case, not a new rule — that handler is not changed by this design.
- **No cascade on delete.** Soft-deleting `O` does not touch its descendants — they keep their own independent lifecycle and remain individually editable/deletable by whoever currently heads them, exactly as `DeleteProjectCommandHandler` already established for Projects ("no cascade... they retain their own independent lifecycle, out of scope here"). This is stated explicitly here, not left implicit, since a tree structure makes cascade-on-delete an easy but wrong assumption to reach for.

Worked example (as dictated by the user): a Manager creates Milestone M and assigns Team Lead T as its Head (`M.OwnerId = T`, `M.ReportingManagerId = Manager`). T creates sub-milestones under M — each one defaults to `OwnerId = T` (§5), so T is their Head, not just their creator's colleague. T has free control over those sub-milestones because **T currently heads them**, not because Manager's authority reaches down to them. T can soft-delete any of *those* sub-milestones directly. T **cannot** soft-delete M itself, because M's Head is T but M's *creator* is the Manager, not T — deleting M requires a request to `M.ReportingManagerId` (the Manager), who approves or rejects it. The Manager, in turn, has no standing authority to reach past T and directly act on T's sub-milestones — the Manager's own free control stopped at M the moment M was handed to T as Head.

## 5. Creation

`POST /api/v1/work/objectives` (create a sub-milestone under an existing Objective): caller must be the parent Objective's current `OwnerId`/Head — uniformly, including when the parent is the Default Objective (whose `OwnerId` is always the Project Lead — see the Default-Objective carve-out below, which makes the "or Project Lead" special-case unnecessary here). The new Objective's `OwnerId` defaults to the creator; the creator may explicitly assign a different Head in the same request (matching the "if they don't assign one, the creator becomes the sub-milestone's in-charge by default" rule from the original raw-context capture). `ReportingManagerId` of the new Objective is always set to the creator, regardless of who is assigned as Head.

**Default-Objective carve-out (resolved during implementation planning, 2026-08-04):** the Default Objective is not a valid target for Edit/Delete/Transfer via endpoints #2/#3/#4 (§6) — reject with `400` ("use the Project endpoints for the Default Objective") if `{id}` resolves to one. It is edited only as a side effect of `PUT /api/v1/work/projects/{id}` (already cascades onto it, per the Slice 2 design) and deleted only as a side effect of `DELETE /api/v1/work/projects/{id}` — both already-designed, unchanged by this document. Its `OwnerId` is permanently the Project's `LeadId` and is never independently transferred: `Objective.OwnerId` is set once, at Project creation (already true — `CreateProjectCommandHandler` sets `defaultObjective.OwnerId = userId`, the same user as `project.LeadId`), and no endpoint in this design writes to it again. This removes the need for any "or Project Lead" special-case anywhere in §4/§6 — creating a child directly under the Default Objective is authorized by the ordinary rule (`caller == parent.OwnerId`) because that `OwnerId` is always the Lead anyway.

## 6. Endpoints

| # | Method + Route | Authorization |
|---|---|---|
| 1 | `POST /api/v1/work/objectives` | `projects:access` + caller is parent's current `OwnerId`/Head. (Parent's `OwnerId` is always the Project Lead when the parent is the Default Objective — see §5's carve-out — so no separate root case is needed here.) |
| 2 | `PUT /api/v1/work/objectives/{id}` | `projects:access` + caller is `{id}`'s current `OwnerId`/Head + `{id}` is not the Default Objective (§5 carve-out — `400` if it is). Non-conflicting edit applies immediately; a conflicting edit creates a `pending` `edit` change request instead (§3/§4) |
| 3 | `DELETE /api/v1/work/objectives/{id}` | `projects:access` + caller is `{id}`'s current `OwnerId`/Head + `{id}` is not the Default Objective (§5 carve-out — `400` if it is). If `caller == {id}.CreatedById` → soft-deletes immediately. Otherwise → creates a `pending` `delete` change request |
| 4 | `POST /api/v1/work/objectives/{id}/transfer` | Same gate as Delete, same Default-Objective carve-out: `caller == {id}.CreatedById` → applies immediately; otherwise → `pending` `transfer` change request. Body: `newHeadUserId` |
| 5 | `POST /api/v1/work/objectives/change-requests/{requestId}/approve` | Caller must equal the change request's `ReportingManagerId`. Applies the underlying action, sets `status = approved` |
| 6 | `POST /api/v1/work/objectives/change-requests/{requestId}/reject` | Same caller check. Sets `status = rejected`, no other state change |
| 7 | `GET /api/v1/work/objectives/change-requests/mine` | `projects:access`. Pending requests where the caller is `ReportingManagerId` — their approval queue |
| 8 | `GET /api/v1/work/projects/{projectId}/objectives` | `projects:access` + caller has an active `project_members` row somewhere in this project's tree (same fallback shape as `GetById` in Slice 2) — the full Objective tree for a Project, for rendering the hierarchy |

The "caller created this Objective" check in #3/#4 is `caller == {id}.CreatedById` (`BaseEntity`, already exists on every entity) — this is the same fact `ReportingManagerId` denormalizes (§3), so #3/#4's own-creation check and the approval-queue's `ReportingManagerId` filter are two different queries reading the same underlying fact from two different, deliberately redundant places for query-shape convenience, not two independent sources of truth.

**Three additional defaults**, stated directly rather than left implicit (same treatment as the deviation notes already recorded in `2026-08-04-work-management-projects-edit-delete-view.md`):
- **At most one `pending` change request per Objective at a time.** #3/#4 must check for an existing `pending` row on `{id}` first and return `409` ("a change request is already pending for this objective") rather than creating a second one — otherwise two competing requests (e.g. Delete then Transfer) could both be approved independently against a state the second approval no longer matches.
- **Approve/reject on an already-decided request is idempotent-of-state, not silently repeatable:** if `status != pending`, #5/#6 return `409` ("this request has already been decided"), matching the same "explicit conflict over silent no-op" convention Project Delete already uses for its own already-deleted check.
- **Transfer never changes `ReportingManagerId`.** #4 reassigns only `OwnerId`; `ReportingManagerId` keeps pointing at the Objective's original creator regardless of how many times headship is transferred afterward — creation is a one-time fact, transfer is not re-creation.

## 7. Out of scope for this design

- Task Management (Task CRUD doesn't exist yet — a separate future Work Management phase per `phase1-table-inventory.md`'s pillar breakdown). The capabilities list from the original raw-context note ("create task", etc.) is not addressed here.
- The broader capability list beyond create/edit/delete/transfer-milestone the user's "etc." implied — this design covers exactly the actions the 2026-08-04 dictation specified. Additional capabilities are a future extension of the same tree model, not a redesign.
- Any UI/frontend concern — this is an API-contract design only.
- The general "Project Lifecycle Workflow, Approval Pipeline, Archive/Restore, Progress Calculation" feature set (manager feedback, captured separately in `next-plan/Project Management.md`) — that is a **different** approval concept (project-level baseline-change/completion approval) from this design's Objective-level delete/edit-conflict/transfer approval. They are related in spirit (both are request→approve flows) but target different entities and different trigger conditions; this design does not attempt to unify them. Whoever designs that feature later should read `objective_change_requests` (§3) first as a possible schema precedent, not assume a shared table.

## 8. Deferred / open items for the implementation plan to resolve

- `PermissionSeeder.cs` still needs `projects:create`/`projects:write` retired and `projects:access` seeded in their place — this is a code change to already-shipped Foundation infrastructure, not covered by this design doc, and needs its own task in the implementation plan with a migration-safe rollout note (existing tenants' `role_permissions` rows referencing the old codes need a data migration, not just a reseed).
- Notification delivery for a pending change request (email? in-app only?) is not specified here — out of scope for the schema/authorization design, in scope for whoever builds the notification.

**Conflict-detection rule, resolved 2026-08-04 (user confirmed during spec review):**
- **Dates:** child's `[StartDate, EndDate]` must fall within the parent's `[StartDate, TargetDate]`, inclusive at both boundaries (a child starting or ending exactly on the parent's boundary date is not a conflict).
- **Hours:** child's `AllocatedHours` must not exceed the parent's `AllocatedHours` — compared against the parent's **total**, not its remaining headroom after siblings. Deliberately simple: no sibling-sum query, consistent with `phase1-table-inventory.md`'s existing warning-only treatment of hours elsewhere in Work Management (this check exists to gate the approval-routing decision, not to enforce a hard capacity budget across siblings).
- **Combination:** a violation of *either* dimension counts as one conflict — the edit as a whole either applies immediately (both checks pass) or goes to one pending `edit` change request (either check fails), not one request per failing dimension.

## 9. Self-review

- No placeholders — every rule traces to an explicit answer given during the 2026-08-04 brainstorming session (permission scope, ownership check, delete mechanism all confirmed via direct back-and-forth, not assumed).
- Internally consistent with the existing "Forbidden: `role`" rule on `project_members` (resolved by reusing `Objective.OwnerId` as Head and adding only `ReportingManagerId`, not a new `project_members` column) and with the already-designed `DeleteProjectCommandHandler` (restated as the tree's root case, not contradicted). Verified against the actual `Objective.cs`/`CreateProjectCommandHandler.cs` code already read this session, not assumed — `OwnerId` was confirmed to already default to the creator before this design was finalized, avoiding a redundant column.
- Scope: this is one coherent vertical slice (schema + tree rule + CRUD + approval endpoints) — large, but not decomposable into independent pieces the way the six original Work Management slices were, since Objective CRUD, the Head/ReportingManager fields, and the approval workflow only make sense together.
- Ambiguity resolved: the "does Edit also need an ownership check" and "how exactly does Delete's reporting-manager requirement work" questions were both open at the start of this brainstorm and are now fully specified in §4/§6, not left as open questions.

**2026-08-04 spec-review pass (requested by the user before committing to an implementation plan, given the scope):** re-read the whole spec fresh and found §4's original rule-1 wording ("free control over everything below `O`") didn't actually match its own worked example — the example only ever shows direct-creator/current-Head control, never one person's authority reaching past an intermediate Head to bypass them. Rewrote §4 to state control-follows-current-headship precisely (fixed above, no user input needed — the worked example the user already gave settled it). Also found and fixed two real gaps that would have blocked writing correct handler code: no stated rule for what happens to descendants when an ancestor Objective is soft-deleted (fixed: no cascade, extending the identical precedent `DeleteProjectCommandHandler` already established for Projects) and three smaller idempotency/consistency gaps (duplicate pending requests, re-deciding an already-decided request, Transfer's effect on `ReportingManagerId`) — all fixed directly with a stated default, the same treatment the sibling Edit/Delete/View plan already gives its own inline deviation notes. The one item that had no existing-code precedent to extend — the exact edit-conflict-detection comparison rule — was put to the user directly rather than guessed, and is now resolved (§8: inclusive date-range containment, total-not-remaining hours comparison, either dimension failing triggers one combined request). **The design has no open items left as of this pass** — ready for `writing-plans`.
