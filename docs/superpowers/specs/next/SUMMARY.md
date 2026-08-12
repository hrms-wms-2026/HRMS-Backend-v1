# specs/next/ — Summary

**Purpose:** Approved designs whose implementation isn't finished yet — either the plan is still `pending`, or no plan has been written. Stays flat, no date subfolders (the date-split is `finished/`-only).

**Last updated:** 2026-08-12

## Files

- `2026-08-03-platform-users-list-design.md` — status: pending, **no plan written yet**. Approved 2026-08-03, sat unplanned since — flag with the user whether it's still wanted.
- `2026-08-07-work-management-objective-subtree-design.md` — physically here but was never listed in this file even before today; its plan (`plans/next/2026-08-07-work-management-objective-subtree.md`) already shipped (git commits `2a77bbc`..`b900ec5`, 2026-08-07). Not moved to `finished/` during the 2026-08-08 pass — out of that session's scope, flagged for a future sync instead.
- `2026-08-12-milestone-to-module-display-rename-design.md` — status: approved 2026-08-12, no plan written yet. Single new EF migration: `module_catalog.name` "Objectives & Milestones" → "Objectives & Modules" for `module_key = 'objectives_milestones'` (key unchanged). Companion to the frontend repo's same-named design doc.

## Open items

- `2026-08-03-platform-users-list-design.md` has no plan anywhere in `plans/` — worth confirming with the user whether it should be planned next, or was silently superseded/abandoned.
- `2026-08-06-work-management-milestone-membership-and-achieve-design.md` moved to `specs/finished/2026-08-08/` on 2026-08-08 (its plan finished all 18 tasks the same day) — see `specs/SUMMARY.md`.
- `2026-08-08-work-management-my-project-milestones-design.md` also moved to `specs/finished/2026-08-08/` the same day (its plan finished all 5 tasks) — see `specs/SUMMARY.md`.
- `2026-08-07-work-management-objective-subtree-design.md` needs a finished/next status sync (see "Files" above).
- `2026-08-10-milestone-ownership-and-subtree-access-design.md` moved to `specs/finished/2026-08-10/` on 2026-08-10 (its plan finished both tasks the same day) — see `specs/SUMMARY.md`.
