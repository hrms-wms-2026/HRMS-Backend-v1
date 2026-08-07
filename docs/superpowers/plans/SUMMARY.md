# plans/ — Summary

**Purpose:** Dated implementation plans, one per feature/fix, following the header format in the `writing-plans` skill (Goal / Architecture / Tech Stack / Global Constraints / numbered tasks with checkboxes). Restructured on 2026-08-06 into `finished/` and `next/` — see `docs/superpowers/rules/FILE_CREATION_RULES.md` for the full file-creation/organization rule this follows.

**Last updated:** 2026-08-06

## Layout

`plans/` has exactly two status folders, per user request:

- `finished/` — completed plans and point-in-time audit/fix reports (42 files), further split into one `YYYY-MM-DD/` subfolder per completion date. See `finished/SUMMARY.md`.
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
| `2026-08-06-work-management-milestone-membership-and-achieve.md` | `next/` | **pending** (0/17 tasks) |
| 26 `*_REPORT.md` / `*_AUDIT_PLAN.md` files (Department, Position, Legal Entity, Dev Smoke, Tenant Provisioning, etc.) | `finished/2026-08-03/` through `finished/2026-08-06/` | finished |
| `Project Management.md` (raw future-feature context, 1 of 2 items still open) | `next/` | pending (not brainstormed) |

See `finished/SUMMARY.md` for the full file-by-file list, grouped by date folder, of all 42 files in that folder (kept short here to avoid duplicating the same list twice).

## Notable finished plans (kept from the pre-restructure summary)

- `finished/2026-08-03/2026-08-03-work-management-foundation.md` — Work Management Slice 1: schema, Domain entities, repositories, and the `POST /api/v1/work/projects` creation transaction (Project + Default Objective + creator membership + Default Version + release reminder + optional labels/logo, atomic).
- `finished/2026-08-04/2026-08-04-work-management-projects-edit-delete-view.md` — Work Management Slice 2: `PUT`/`DELETE`/`GET`-by-id/`GET mine`/`GET ?userId=` on `ProjectsController`. **Executed 2026-08-05**: all 9 tasks + final review + one fix wave, re-reviewed clean.
- `finished/2026-08-04/2026-08-04-work-management-milestone-hierarchy.md` — Work Management's Milestone/Objective hierarchy: `PermissionSeeder.cs` retrofit, `Objective.ReportingManagerId` + `objective_change_requests` schema, recursive tree-authorization, 8 endpoints. **Executed 2026-08-05/06**: all 14 tasks + final review + one fix wave, re-reviewed clean. Three Important findings were parked as real product/design gaps — resolved by the plan below.

## Next up

- `next/2026-08-06-work-management-milestone-membership-and-achieve.md` — closes the three gaps parked at the end of the milestone-hierarchy plan's final review (Head assignment not becoming project membership, `GetObjectiveTree` scope leak, `ReportingManagerId` not tracking the parent's current Head) and adds a new Achieve completion workflow for Projects/Objectives. Built from `specs/2026-08-06-work-management-milestone-membership-and-achieve-design.md`. 17 tasks, not yet started.

## Open items

- Phase 2 (remaining `ONEVO_Backend_Architecture_Document.md` sections: tenant isolation, caching, file handling, performance, testing, deployment) and Phase 3 (structural table-existence check of `phase1-table-inventory.md`) are queued in `finished/2026-08-03/2026-08-03-doc-audit-and-process-setup.md` Tasks 6-7 but not yet executed.
- Confirmed but explicitly deferred test-coverage gaps (user chose Invitation flow only for this pass): `PermissionsController` (zero coverage), Stripe webhook event processing (`ProcessStripeEventCommandHandler`), and a ~50-handler long tail in the DevPlatform admin backoffice. See `finished/2026-08-04/2026-08-04-test-suite-audit-and-invite-coverage.md` for the full inventory.
- Execution order for Work Management: Foundation (done) → Slice 2 (done) → Milestone Hierarchy (done) → `next/2026-08-06-work-management-milestone-membership-and-achieve.md` (not started — next up).
- The broader project lifecycle/approval/progress-tracking feature set (workflow status, approval pipeline, archive/restore, progress calculation) remains raw context, not yet brainstormed, in `next/Project Management.md` — separate from and not to be silently merged with the milestone-hierarchy plan's `objective_change_requests` table. The new Achieve workflow is a narrow, single-flag addition and deliberately does not attempt to unify with that broader, still-unbrainstormed feature set.
- The 42 files in `finished/` were classified by filename convention (and, for the date-folder split, by explicit `**Date:**` headers or content inference) during the 2026-08-06 move, not individually re-verified — see `finished/SUMMARY.md`'s status note and "Layout: date subfolders" section for which dates were inferred.
