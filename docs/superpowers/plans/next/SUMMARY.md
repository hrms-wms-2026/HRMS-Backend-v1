# plans/next/ — Summary

**Purpose:** Everything in `plans/` that is not finished yet. Two kinds of content live here side by side:
1. Plans that have been written and started but whose implementation isn't done (status: `pending`).
2. Raw context for features flagged during other work as "not now, but don't lose this" — not designs or plans yet (those go through `superpowers:brainstorming` → `docs/superpowers/specs/` → a plan file here when actually started).

This folder absorbed the former top-level `docs/superpowers/next-plan/` folder on 2026-08-06 as part of the `plans/` restructure into `finished/` and `next/` — see `FILE_CREATION_RULES.md` in `docs/superpowers/rules/`.

**Last updated:** 2026-08-12

## Files

- `2026-08-12-milestone-to-module-display-rename.md` — status: pending, not started. Single-task plan: one new EF migration renaming `module_catalog.name` for `module_key = 'objectives_milestones'` from "Objectives & Milestones" to "Objectives & Modules". Design: `specs/next/2026-08-12-milestone-to-module-display-rename-design.md`. Companion to the frontend repo's same-named plan.
- `2026-08-12-objective-viewmodel-owner-fields-fix.md` — status: pending, not started. Single-task plan: add `OwnerName`/`ReportingManagerName`/`IsOwner` (+ `IsAchieved`/`AchievedAt` on the subtree node) to `ObjectiveDetailViewModel`/`ObjectiveSubtreeNodeViewModel` and forward them in `ObjectiveViewModelMapper` — fields the Application layer already computes but the wire contract drops. Design: `specs/next/2026-08-12-objective-viewmodel-owner-fields-fix-design.md`. Blocks the frontend repo's `2026-08-12-milestone-tree-mockup-redesign.md`.
- `Project Management.md` — two future features, both raw context, not designs:
  1. Milestone (Objective) In-Charge Role & Permission System — **superseded**, see the file's own header: fully brainstormed and designed in `specs/2026-08-04-work-management-milestone-hierarchy-design.md`.
  2. Project Lifecycle Workflow, Approval Pipeline, Archive/Restore, and Progress Calculation — not started. Backend-relevant subset of a manager corrections doc on the Project Management user journey (2026-08-04). Blocked on Objective/Task CRUD existing first; conflicts with the schema's current "forbidden: free-form status" note on `projects`.

## Known drift

- The previous version of this summary (when the folder was `docs/superpowers/next-plan/`) also listed a `Notification Management (Outbox Mapping).md` file. That file does not exist anywhere in the repo — the summary entry was never backed by an actual file. If that context still matters, it needs to be re-captured from scratch; nothing was lost in this move, the source file was already missing before 2026-08-06.
- `2026-08-07-work-management-objective-subtree.md` sits in this folder (flat, per rule) but its git commits (`2a77bbc`..`b900ec5`, 2026-08-07) show it already shipped — it was never moved to `finished/` or given a status row in `plans/SUMMARY.md`. Not touched during the 2026-08-08 pass since it's outside that session's scope; flagged here so it isn't lost again.
- `2026-08-10-milestone-ownership-and-subtree-access.md` moved to `plans/finished/2026-08-10/` on 2026-08-10 (both tasks done) — see `plans/SUMMARY.md`.
- `2026-08-10-project-detail-milestone-tree-view-backend.md` moved to `plans/finished/2026-08-10/` on 2026-08-10 (all 3 tasks done, 170/170 WorkManagement unit tests) — see `plans/SUMMARY.md`.

## Open items

- Neither remaining raw-context item above is brainstormed yet. See each one's "Suggested next step" section in `Project Management.md`.
- `2026-08-06-work-management-milestone-membership-and-achieve.md` moved to `plans/finished/2026-08-08/` on 2026-08-08 (all 18 tasks done) — see `plans/SUMMARY.md`.
- `2026-08-08-work-management-my-project-milestones.md` also moved to `plans/finished/2026-08-08/` the same day (all 5 tasks done) — see `plans/SUMMARY.md`.
- `2026-08-07-work-management-objective-subtree.md`'s finished/next status needs syncing (see "Known drift" above) — separate follow-up, not part of the 2026-08-08 work.
- `2026-08-08-work-management-frontend-blocking-endpoints.md` moved to `plans/finished/2026-08-09/` on 2026-08-09 — both requested items (List Project Categories endpoint, `isAchieved`/`achievedAt` on List Projects) shipped same day. See `plans/SUMMARY.md`.
