# plans/next/ — Summary

**Purpose:** Everything in `plans/` that is not finished yet. Two kinds of content live here side by side:
1. Plans that have been written and started but whose implementation isn't done (status: `pending`).
2. Raw context for features flagged during other work as "not now, but don't lose this" — not designs or plans yet (those go through `superpowers:brainstorming` → `docs/superpowers/specs/` → a plan file here when actually started).

This folder absorbed the former top-level `docs/superpowers/next-plan/` folder on 2026-08-06 as part of the `plans/` restructure into `finished/` and `next/` — see `FILE_CREATION_RULES.md` in `docs/superpowers/rules/`.

**Last updated:** 2026-08-16

## Files

- `2026-08-12-milestone-to-module-display-rename.md` — status: pending, not started. Single-task plan: one new EF migration renaming `module_catalog.name` for `module_key = 'objectives_milestones'` from "Objectives & Milestones" to "Objectives & Modules". Design: `specs/next/2026-08-12-milestone-to-module-display-rename-design.md`. Companion to the frontend repo's same-named plan.
- `2026-08-12-objective-viewmodel-owner-fields-fix.md` — status: pending, not started. Single-task plan: add `OwnerName`/`ReportingManagerName`/`IsOwner` (+ `IsAchieved`/`AchievedAt` on the subtree node) to `ObjectiveDetailViewModel`/`ObjectiveSubtreeNodeViewModel` and forward them in `ObjectiveViewModelMapper` — fields the Application layer already computes but the wire contract drops. Design: `specs/next/2026-08-12-objective-viewmodel-owner-fields-fix-design.md`. Blocks the frontend repo's `2026-08-12-milestone-tree-mockup-redesign.md`.
- `2026-08-16-employee-detail-screen-backend.md` — status: pending, not started. Built from `specs/next/2026-08-16-employee-detail-screen-backend-design.md`. Sub-project 2 of 2 in the decomposed employee-detail/invitation/cross-entity feature request; depends on the finished `2026-08-16-multi-legal-entity-employment-foundation` plan.
- `Project Management.md` — two future features, both raw context, not designs:
  1. Milestone (Objective) In-Charge Role & Permission System — **superseded**, see the file's own header: fully brainstormed and designed in `specs/2026-08-04-work-management-milestone-hierarchy-design.md`.
  2. Project Lifecycle Workflow, Approval Pipeline, Archive/Restore, and Progress Calculation — not started. Backend-relevant subset of a manager corrections doc on the Project Management user journey (2026-08-04). Blocked on Objective/Task CRUD existing first; conflicts with the schema's current "forbidden: free-form status" note on `projects`.

## Known drift

- The previous version of this summary (when the folder was `docs/superpowers/next-plan/`) also listed a `Notification Management (Outbox Mapping).md` file. That file does not exist anywhere in the repo — the summary entry was never backed by an actual file. If that context still matters, it needs to be re-captured from scratch; nothing was lost in this move, the source file was already missing before 2026-08-06.
- `2026-08-07-work-management-objective-subtree.md` sits in this folder (flat, per rule) but its git commits (`2a77bbc`..`b900ec5`, 2026-08-07) show it already shipped — it was never moved to `finished/` or given a status row in `plans/SUMMARY.md`. Not touched during the 2026-08-08 pass since it's outside that session's scope; flagged here so it isn't lost again.
- `2026-08-10-milestone-ownership-and-subtree-access.md` moved to `plans/finished/2026-08-10/` on 2026-08-10 (both tasks done) — see `plans/SUMMARY.md`.
- `2026-08-10-project-detail-milestone-tree-view-backend.md` moved to `plans/finished/2026-08-10/` on 2026-08-10 (all 3 tasks done, 170/170 WorkManagement unit tests) — see `plans/SUMMARY.md`.
- `2026-08-17-work-management-task-data-seeding.md` — extends `WorkManagementDapiDemoSeeder` with leaf tasks, project+objective task statuses, and Approvals queue (pending task-creation + extend_allocation). Design: `specs/next/2026-08-17-work-management-task-data-seeding-design.md`.
- `2026-08-20-work-management-tree-sprint-task-unified-view/` — status: backend Parts 1-6 all shipped and
  verified (428/428 WorkManagement tests, per `git log`); frontend Parts 1-7 (own plan folder in the
  frontend repo) also all shipped and verified (443/443 Work module tests). Code-complete on both sides —
  **still pending the manual browser pass** called out in the combined Cursor execution prompt before
  either plan folder moves to `finished/`. Parts 1-2: `part-1-fix-sprint-and-task-authorization.md` (security fix:
  `GetObjectiveSprintsQueryHandler`/`GetObjectiveTasksQueryHandler` had no membership/reachability check,
  only `IsAuthenticated`), `part-2-get-sprint-tasks-endpoint.md` (new `GET /work/sprints/{id}/tasks`).
  Parts 3-5 (new): `part-3-enrich-objective-tree-response.md` (add `Progress`/`OwnerName`/per-node `IsOwner`
  to `GetObjectiveTreeQuery`'s response — the frontend needs this to switch the Tree tab to the
  project-wide tree endpoint and gate action icons per-branch), `part-4-delete-task-soft-delete.md`
  (no migration needed — `WorkTask` already has unused `IsDeleted`/`DeletedAt` via `BaseEntity`, just needs
  a `Remove()` wired up), `part-5-sprint-optional-task-creation.md` (loosen `SprintId` validation across 3
  independent code paths: direct create, create-request, and request-approval), `part-6-postman-doc-gaps.md`
  (done 2026-08-21, docs-only — filled 4 previously-undocumented live Sprint endpoints: Create/Edit/
  Achieve/Complete Sprint). Design:
  `specs/next/2026-08-20-work-management-tree-sprint-task-unified-view-design.md` (§4 rewritten 2026-08-21
  for the expanded requirement — 6/4/2 row icon sets, restored side panel, project-wide tree switch,
  icon-visibility scoped to the caller's own branch). Frontend plan (4 new parts, `part-4` through
  `part-7`) in the frontend repo: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/`
- `2026-08-21-work-management-cascading-objective-ownership/` — status: pending, not started. 5 parts,
  backend-only. Design: `specs/next/2026-08-21-work-management-cascading-objective-ownership-design.md`.
  `part-1-effective-manager-helper.md` adds `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync`
  (owner-or-member of this Objective or any ancestor, walking `ParentObjectiveId` up); Parts 2-4 replace
  the repeated `objective.OwnerId != callerEmployeeId.Value` check with it across sub-module create/edit,
  member management, Sprint create/edit/achieve/complete, Task create/delete, and the
  `MoveTaskStatus` private-status gate; Part 5 cascades the tree's `IsOwner` display flag the same way
  and runs the full-feature regression pass. Do the Parts in order (1 is a hard prerequisite for 2-5;
  2-4 are independent of each other; 5 must be last).
- `2026-08-21-work-management-project-scoped-task-status-and-category/` — status: pending, not started
  (plan not yet written — design only so far). Design:
  `specs/next/2026-08-21-work-management-project-scoped-task-status-and-category-design.md`. Collapses
  Task Status from per-Objective to per-Project (no destructive migration), adds a new per-Project
  `TaskCategory` entity replacing the hardcoded `WorkTaskTypes` enum (migration + backfill required).
  Frontend companion plan (new Project-level Settings page) lives in the frontend repo, also not yet
  written.
  — its own Parts 1-3 (node model, shared icon set, carousel+panel removal) already shipped 2026-08-21 and
  are superseded/reversed in part by the new Parts 4-7.
- `2026-08-20-work-management-project-page-redesign/` moved to `plans/finished/2026-08-21/` on 2026-08-21 — backend (3 parts) + frontend (3 parts, own plan in the frontend repo) both shipped, and the user completed a full manual browser test 2026-08-21 ("perfect, OK"). See `plans/SUMMARY.md`. **Note:** this SUMMARY and its siblings (`specs/next/SUMMARY.md`, `plans/SUMMARY.md`) are known to lag `git log` on this repo (see `feedback_hrms_docs_lag_git` convention) — several plans/specs listed above as "pending, not started" may already be implemented; verify against `git log --oneline` before trusting any status in this file.

## Open items

- Neither remaining raw-context item above is brainstormed yet. See each one's "Suggested next step" section in `Project Management.md`.
- `2026-08-06-work-management-milestone-membership-and-achieve.md` moved to `plans/finished/2026-08-08/` on 2026-08-08 (all 18 tasks done) — see `plans/SUMMARY.md`.
- `2026-08-08-work-management-my-project-milestones.md` also moved to `plans/finished/2026-08-08/` the same day (all 5 tasks done) — see `plans/SUMMARY.md`.
- `2026-08-07-work-management-objective-subtree.md`'s finished/next status needs syncing (see "Known drift" above) — separate follow-up, not part of the 2026-08-08 work.
- `2026-08-08-work-management-frontend-blocking-endpoints.md` moved to `plans/finished/2026-08-09/` on 2026-08-09 — both requested items (List Project Categories endpoint, `isAchieved`/`achievedAt` on List Projects) shipped same day. See `plans/SUMMARY.md`.
- `2026-08-16-multi-legal-entity-employment-foundation/` moved to `plans/finished/2026-08-16/` on 2026-08-16 — all 3 parts executed; unit suite 2110/2110. See `plans/SUMMARY.md`.
