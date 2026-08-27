# plans/next/ — Summary

**Purpose:** Everything in `plans/` that is not finished yet. Two kinds of content live here side by side:
1. Plans that have been written and started but whose implementation isn't done (status: `pending`).
2. Raw context for features flagged during other work as "not now, but don't lose this" — not designs or plans yet (those go through `superpowers:brainstorming` → `docs/superpowers/specs/` → a plan file here when actually started).

This folder absorbed the former top-level `docs/superpowers/next-plan/` folder on 2026-08-06 as part of the `plans/` restructure into `finished/` and `next/` — see `FILE_CREATION_RULES.md` in `docs/superpowers/rules/`.

**Last updated:** 2026-08-27

## Files

- `2026-08-27-attendance-history-redesign-backend.md` — status: pending, not started. Built from `specs/next/2026-08-27-attendance-history-redesign-backend-design.md`. 2 tasks: `GetAttendanceDayDetailQuery` (DTOs + handler, extends `AttendanceReadHandler`) and the `GET .../history-detail` controller endpoint it backs. Self-access always allowed; others require `attendance:read`+visibility for summary/timeline and additionally `monitoring:read` for activity (nulled, not 403'd, if absent). Companion: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-27-attendance-history-redesign-frontend/`.
- `2026-08-21-leave-management/` — status: Phase 0+1 executed 2026-08-21; Phase 2 executed 2026-08-21 (Leave Policies on `feat/leave-management-part-2`); Phase 3 executed 2026-08-21 (Entitlements + Balances on `feat/leave-management-part-3`, unit/architecture verified; live Docker smoke pending); Phase 4 executed 2026-08-22 (`feat/leave-management-part-4`, unit/architecture verified; live Docker smoke pending); Phase 5 executed 2026-08-22 (`feat/leave-management-part-5`, unit/architecture verified; live Docker smoke pending); Phase 6 executed 2026-08-22 (`feat/leave-management-part-6`, live Docker smoke pending); Phase 7 executed 2026-08-23 (Team Calendar backend, unit/architecture/API build verified; Testcontainers smoke compiles but live run pending Docker engine); Phase 8 executed 2026-08-23 (Balance Audit + CSV export + year-end job on `feat/leave-management-part-8`, unit suite 3105/3105 and architecture suite 676/676 green, live Docker smoke pending); Phase 9 executed 2026-08-24 on `feat/leave-management-part-9` (LeaveTypesController architecture tests, LeaveBalanceMapping N+1 guard, GetLeaveTypeQueryHandler tests; live Priya HTTP e2e blocked — Docker engine down, local OnevoDb schema behind this branch). Full 10-phase Leave Management build (Screens 1-9, 5 stored records + full product-surface fields, per `C:\HR\leave-management-complete.md`). Design: `specs/next/2026-08-21-leave-management-design.md`. Written in full: `part-1-schema-and-leave-types.md` (Phase 0 schema for all 11 tables + Phase 1 Leave Types CRUD, includes a data-patch migration fixing the `HR Manager` role template's missing `leave:manage` permission), `part-2-leave-policies.md` (Phase 2 Leave Policies backend, including the additive request-driven `accrual_method` schema correction and explicit no-hardcoded-business-values guard), `part-3-entitlements-and-balances.md` (Phase 3 Entitlements + Balances backend, with request/policy/config-driven values and no production handler defaults), `part-4-request-submission.md` (Phase 4 Leave Request Submission backend, with request/policy/provider/config-driven backdating, unpaid split, reason requirement, working days, holidays, calendar conflicts, team absence warnings, and pending paid-day reservation), `part-5-approval-workflow.md` (Phase 5 Approval Workflow backend, with approval-mode enforcement, paid-day pending-to-used movement, request-info pause/resume storage, config-driven self-approval, and registered outbox side-effect handlers), `part-6-cancellation.md` (Phase 6 Cancellation backend, with pending reservation release, approved paid-day restoration, stored day allocations for partial cancellation, legal-entity/config-driven business date, HR reason requirement, and `xmin` concurrency), `part-7-team-calendar.md` (Phase 7 Team Calendar backend, with scoped month view, config-driven tentative blocks and type colors, public holidays through a provider, department filter, and no new calendar entity), `part-8-balance-audit-and-year-end.md` (Phase 8: balance-audit read + CSV export, entitlement-generation-preview CSV export, and an automatic year-end entitlement job that reuses Phase 3's calculator/planner directly rather than through `IMediator`, since the command handler needs `ICurrentUser` which a background job doesn't have), and `part-9-hardening.md` (Phase 9: audited Phases 0-8 against the architecture skill's NFR checklist — found and fixed a real architecture-test gap for `LeaveTypesController`, found the previously-flagged "balances N+1 risk" was already non-existent in the shipped code and added a regression guard instead of a fix, found the "retire mocked frontend" task was based on a stale assumption since nothing frontend was ever built, and scripted a full live-dev-DB run of the spec's Priya worked example). Companion: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-21-leave-management/`. Supersedes the narrower mocked-data Phase 1 sketch in the frontend repo's `specs/next/2026-08-17-attendance-leave-management-design.md`.
- `2026-08-19-sensitive-position-approval-bypass.md` — status: pending, not started. Built from `specs/next/2026-08-19-sensitive-position-approval-bypass-design.md`. 3 tasks: `ChangeEmployeePositionCommandHandler` bypass (transfer), `OnboardingDraftWriteService` bypass (onboarding finalize), integration coverage. Depends on `2026-08-17-sensitive-position-approval-backend.md` below being implemented first (extends its `AccessGrantRequest`/`roles:manage` machinery). Note: touches `OnboardingDraftWriteService.cs` and its test file, both of which have unrelated uncommitted local changes on this branch as of 2026-08-19 (a transactional refactor from other in-progress work) — re-read current state before editing.
- `2026-08-17-sensitive-position-approval-backend.md` — status: pending, not started. Built from `specs/next/2026-08-17-sensitive-position-approval-backend-design.md`. Routes sensitive Change Position requests through the existing AccessGrantRequest approval machinery, gated on `roles:manage`; blocks an employee from ever changing their own position; adds `employeeId` to `GET /auth/me` for the frontend companion. Companion: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/2026-08-17-sensitive-position-approval-frontend.md`.
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
- `2026-08-21-work-management-cascading-objective-ownership/` — status: **code-complete, shipped
  2026-08-22, still in `next/` pending a manual browser pass** (no frontend code changes in this design,
  but the tree UI's cascaded action icons can only be confirmed by looking at it as a non-owner
  ancestor-member). 5 parts, backend-only. Design:
  `specs/next/2026-08-21-work-management-cascading-objective-ownership-design.md`.
  `part-1-effective-manager-helper.md` added `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync`
  (owner-or-member of this Objective or any ancestor, walking `ParentObjectiveId` up); Parts 2-4 replaced
  the repeated `objective.OwnerId != callerEmployeeId.Value` check with it across sub-module create/edit,
  member management, Sprint create/edit/achieve/complete, Task create/delete, and the
  `MoveTaskStatus` private-status gate; Part 5 cascaded the tree's `IsOwner` display flag the same way
  and ran the full-feature regression pass. Each Part's own mandated verification grep (or the
  controller's pre-flight run of it) found more real call sites than the plan/spec's own estimate every
  time it was run — 27 handlers converted in total across all 5 Parts, vs. the design's original ~9-call-
  site estimate — see each part file's own `**Status:**` line for its exact handler list. 469/469
  WorkManagement unit tests passing, `dotnet build` clean, module-wide grep for the old direct-owner
  pattern returns zero matches. 18 Postman docs updated for the new cascaded-authorization wording.
- `2026-08-21-work-management-project-scoped-task-status-and-category/` — status: pending, not started.
  4 parts, backend-only (frontend companion plan not yet written, lives in the frontend repo). Design:
  `specs/next/2026-08-21-work-management-project-scoped-task-status-and-category-design.md`.
  `part-1-collapse-task-status-to-project-scope.md` re-scopes all 5 Task Status endpoints from
  `ObjectiveId` to `ProjectId` (depends on the cascading-ownership plan's Part 1
  `IsEffectiveManagerAsync`), stops seeding per-Objective copies. `part-2-task-category-entity-and-migration.md`
  adds the new `TaskCategory` entity + migration + default-4-row seeding at Project creation.
  `part-3-worktask-categoryid-migration-and-wiring.md` migrates `WorkTask.TaskType` (string) to
  `CategoryId` (Guid) with a SQL backfill, then sweeps ~21 call sites. `part-4-task-category-crud-and-docs.md`
  adds the 5 Task Category CRUD endpoints mirroring Task Status, plus Postman docs. Do Parts 1-2 in
  either order, Part 3 needs Part 2 first, Part 4 needs both Part 1 (authorization shape) and Part 2.
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
