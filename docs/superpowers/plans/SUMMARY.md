# plans/ — Summary

**Purpose:** Dated implementation plans, one per feature/fix, following the header format in the `writing-plans` skill (Goal / Architecture / Tech Stack / Global Constraints / numbered tasks with checkboxes). Restructured on 2026-08-06 into `finished/` and `next/` — see `docs/superpowers/rules/FILE_CREATION_RULES.md` for the full file-creation/organization rule this follows.

**Last updated:** 2026-08-21

## Layout

`plans/` has exactly two status folders, per user request:

- `finished/` — completed plans and point-in-time audit/fix reports (43 files), further split into one `YYYY-MM-DD/` subfolder per completion date. See `finished/SUMMARY.md`.
- `next/` — plans not yet finished (status `pending`) plus raw not-yet-brainstormed future-feature context. Stays flat, no date subfolders (the date-split is `finished/`-only, per user request). See `next/SUMMARY.md`.

`plans/kajaa/` (a personal plan folder for one team member) existed briefly during the 2026-08-06 restructure but was dissolved the same day — its 3 finished plans were folded into `finished/` so that `finished/`/`next/` are the only two categories.

## Total plan list + status

| Plan | Location | Status |
|---|---|---|
| `2026-07-27-forgot-password-restricted-role-http-rls-proof.md` | `finished/2026-07-27/` | finished |
| `2026-07-28-legal-document-rich-content.md` | `finished/2026-07-28/` | finished |
| `2026-07-28-tenant-host-password-login-retirement.md` | `finished/2026-07-28/` | finished |
| `2026-08-02-dev-smoke-multi-tenant-seed-expansion.md` | `finished/2026-08-02/` | finished |
| `2026-08-03-doc-audit-and-process-setup.md` | `finished/2026-08-03/` | finished |
| `2026-08-03-view-model-retrofit-phase1.md` | `finished/2026-08-03/` | finished |
| `2026-08-03-work-management-foundation.md` | `finished/2026-08-03/` | finished |
| `2026-08-04-department-hardening-part1.md` | `finished/2026-08-04/` | finished |
| `2026-08-04-department-hardening-part2.md` | `finished/2026-08-04/` | finished |
| `2026-08-04-department-hardening-part3.md` | `finished/2026-08-04/` | finished |
| `2026-08-04-position-foundation-part2b.md` | `finished/2026-08-04/` | finished |
| `2026-08-04-test-suite-audit-and-invite-coverage.md` | `finished/2026-08-04/` | finished |
| `2026-08-04-tray-employee-check-in.md` | `finished/2026-08-04/` | finished |
| `2026-08-04-work-management-milestone-hierarchy.md` | `finished/2026-08-04/` | finished |
| `2026-08-04-work-management-projects-edit-delete-view.md` | `finished/2026-08-04/` | finished |
| `2026-08-05-activity-monitoring-keyboard-mouse-tracking.md` | `finished/2026-08-05/` | finished |
| `2026-08-05-dev-smoke-employee-seeding.md` | `finished/2026-08-05/` | finished |
| `2026-08-05-remove-legacy-employee-job-title-manager-fields.md` | `finished/2026-08-05/` | finished |
| `2026-08-06-legal-entity-permission-access-filter.md` | `finished/2026-08-06/` | finished |
| `2026-08-06-work-management-milestone-membership-and-achieve.md` | `finished/2026-08-08/` | finished (18/18 tasks) |
| `2026-08-08-work-management-my-project-milestones.md` | `finished/2026-08-08/` | finished (5/5 tasks) |
| `2026-08-08-work-management-frontend-blocking-endpoints.md` | `finished/2026-08-09/` | finished (2/2 items shipped) |
| 26 `*_REPORT.md` / `*_AUDIT_PLAN.md` files (Department, Position, Legal Entity, Dev Smoke, Tenant Provisioning, etc.) | `finished/2026-08-03/` through `finished/2026-08-06/` | finished |
| `Project Management.md` (raw future-feature context, 1 of 2 items still open) | `next/` | pending (not brainstormed) |
| `2026-08-10-milestone-ownership-and-subtree-access.md` | `finished/2026-08-10/` | finished (2/2 tasks) |
| `2026-08-10-project-detail-milestone-tree-view-backend.md` | `finished/2026-08-10/` | finished (3/3 tasks) |
| `2026-08-12-milestone-to-module-display-rename.md` | `next/` | pending (not started) |
| `2026-08-16-multi-legal-entity-employment-foundation/` (3 parts) | `finished/2026-08-16/` | finished |
| `2026-08-16-employee-detail-screen-backend.md` | `next/` | pending (not started) |
| `2026-08-21-leave-management/` (Parts 1-2 executed) | `next/` | Phase 0+1 and Phase 2 executed 2026-08-21; Phases 3-9 pending |

See `finished/SUMMARY.md` for the full file-by-file list, grouped by date folder, of all files in that folder (kept short here to avoid duplicating the same list twice).

## Notable finished plans (kept from the pre-restructure summary)

- `finished/2026-08-03/2026-08-03-work-management-foundation.md` — Work Management Slice 1: schema, Domain entities, repositories, and the `POST /api/v1/work/projects` creation transaction (Project + Default Objective + creator membership + Default Version + release reminder + optional labels/logo, atomic).
- `finished/2026-08-04/2026-08-04-work-management-projects-edit-delete-view.md` — Work Management Slice 2: `PUT`/`DELETE`/`GET`-by-id/`GET mine`/`GET ?userId=` on `ProjectsController`. **Executed 2026-08-05**: all 9 tasks + final review + one fix wave, re-reviewed clean.
- `finished/2026-08-04/2026-08-04-work-management-milestone-hierarchy.md` — Work Management's Milestone/Objective hierarchy: `PermissionSeeder.cs` retrofit, `Objective.ReportingManagerId` + `objective_change_requests` schema, recursive tree-authorization, 8 endpoints. **Executed 2026-08-05/06**: all 14 tasks + final review + one fix wave, re-reviewed clean. Three Important findings were parked as real product/design gaps — resolved by the plan below.
- `finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve.md` — closes the three gaps parked at the end of the milestone-hierarchy plan's final review (Head assignment not syncing project membership, `GetObjectiveTree` scope leak, `ReportingManagerId` frozen instead of tracking the parent's current Head) and adds a new Achieve/Unachieve completion workflow for both Projects and Objectives. Built from `specs/finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve-design.md`. **Executed 2026-08-08**: all 18 tasks, full unit suite green (1621/1621) after every task, one commit per task. Integration test coverage (Task 16) initially could not be verified green — every test in `CreateProjectEndpointTests` failed on `POST /api/v1/work/projects` returning `403`, reproduced identically at the pre-Task-10 commit (`1b8adaa`) in an isolated worktree, confirming it predated this plan's code. Root-caused and fixed the same day (commit `5ec9a91`): `DefaultRoleSeeder` only grants permissions whose module matches the subscribed plan's `included_modules_json`, which uses the canonical Phase 1 module keys since the 2026-08-04 taxonomy rename — but every Work Management permission is still tagged the legacy `work_management` module, so no plan (including the test fixture's seeded one) ever grants them. Test fixture patched to grant the missing permissions directly; the underlying module-tag mismatch likely affects real tenant signups too and is flagged, not fixed, as a separate production concern. All 27 integration tests now pass.
- `finished/2026-08-08/2026-08-08-work-management-my-project-milestones.md` — new read endpoint, `GET /api/v1/work/projects/{projectId}/objectives/mine` — every milestone the caller has ever had a `project_members` row for in a given project (any status, frontend filters), with owner/reporting-manager names resolved server-side. Brainstormed and planned 2026-08-08 immediately after the membership/Achieve plan finished; built from `specs/finished/2026-08-08/2026-08-08-work-management-my-project-milestones-design.md`. **Executed 2026-08-08**: all 5 tasks, full unit suite green (1629/1629) after every task, one commit per task.
- `finished/2026-08-09/2026-08-08-work-management-frontend-blocking-endpoints.md` — new `GET /api/v1/work/project-categories` endpoint plus `isAchieved`/`achievedAt` added to `GET /work/projects/mine`'s response, both requested by the frontend team while brainstorming the Work Management Projects UI. **Executed 2026-08-09** same-session on direct user request (no separate brainstorm/spec — both items were already fully specified in the request doc itself): `dotnet build` clean, new `ListProjectCategoriesQueryHandlerTests.cs` added, 160/160 WorkManagement unit tests passing.

- `finished/2026-08-16/2026-08-16-multi-legal-entity-employment-foundation/` — invitation capacity reservation, cross-legal-entity invitation (same person, two `Employee` rows, one `User`), and session active-company permission recompute. Built from `specs/finished/2026-08-16/2026-08-16-multi-legal-entity-employment-foundation-design.md`. **Executed 2026-08-16**: all 3 parts; unit suite 2110/2110; `employees.user_id` unique index replaced with `(user_id, legal_entity_id)`. Frontend company-selector in the sibling web repo posts `POST /api/v1/session/active-company` then refreshes `/auth/me`.

## Next up

- The multi-legal-entity employment foundation shipped 2026-08-16. Sub-project 2 (Employee Detail screen, `employees:read:sensitive`, Change Position) has both a design and a plan (`next/2026-08-16-employee-detail-screen-backend.md`) but hasn't started implementation yet.
- Nothing currently in flight for Work Management backend. The only remaining `next/` items are the two raw, not-yet-brainstormed `Project Management.md` entries (see `next/SUMMARY.md`), plus the still-unsynced `2026-08-07-work-management-objective-subtree.md` drift noted below.
- `2026-08-10-milestone-ownership-and-subtree-access.md` (finished 2026-08-10) unblocks the frontend's Milestone Cards + Tree View feature — see `Hrms--Web-application---front-end---v1/docs/superpowers/plans/finished/2026-08-10/2026-08-10-milestone-cards-and-tree-view/`.
- A separate, smaller plan — `docs/superpowers/plans/next/2026-08-07-work-management-objective-subtree.md` (`GET /api/v1/work/objectives/{id}/tree`) — is not reflected in this SUMMARY's status table at all, though its git commits (`2a77bbc`..`b900ec5`, 2026-08-07) show it already shipped. This predates the current session and wasn't touched during this pass; worth a status-sync check next time `plans/` is audited.

## Open items

- Phase 2 (remaining `ONEVO_Backend_Architecture_Document.md` sections: tenant isolation, caching, file handling, performance, testing, deployment) and Phase 3 (structural table-existence check of `phase1-table-inventory.md`) are queued in `finished/2026-08-03/2026-08-03-doc-audit-and-process-setup.md` Tasks 6-7 but not yet executed.
- Confirmed but explicitly deferred test-coverage gaps (user chose Invitation flow only for this pass): `PermissionsController` (zero coverage), Stripe webhook event processing (`ProcessStripeEventCommandHandler`), and a ~50-handler long tail in the DevPlatform admin backoffice. See `finished/2026-08-04/2026-08-04-test-suite-audit-and-invite-coverage.md` for the full inventory.
- Execution order for Work Management: Foundation (done) → Slice 2 (done) → Milestone Hierarchy (done) → Membership/Achieve (done 2026-08-08). The Get Objective Subtree plan (`next/2026-08-07-work-management-objective-subtree.md`) shipped in between but its own status tracking was never synced — see the "Next up" note above.
- The broader project lifecycle/approval/progress-tracking feature set (workflow status, approval pipeline, archive/restore, progress calculation) remains raw context, not yet brainstormed, in `next/Project Management.md` — separate from and not to be silently merged with the milestone-hierarchy plan's `objective_change_requests` table. The new Achieve workflow is a narrow, single-flag addition and deliberately does not attempt to unify with that broader, still-unbrainstormed feature set.
- The 42 pre-2026-08-08 files in `finished/` were classified by filename convention (and, for the date-folder split, by explicit `**Date:**` headers or content inference) during the 2026-08-06 move, not individually re-verified — see `finished/SUMMARY.md`'s status note and "Layout: date subfolders" section for which dates were inferred.
- The `docs/postman-request/README.md` module index was stale from 2026-08-08 through 2026-08-09 (listed only `Create Project.md`) — fixed 2026-08-09 alongside the frontend-blocking-endpoints work; now correctly shows 24 files under `Work Management/`.
