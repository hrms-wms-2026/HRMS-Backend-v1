# specs/ — Summary

**Purpose:** Approved designs, produced by `superpowers:brainstorming`, that a `docs/superpowers/plans/` implementation plan is then built from. This folder had no `SUMMARY.md` until 2026-08-06, when it was also split into `finished/`/`next/` (mirroring the `plans/` restructure — see `docs/superpowers/rules/FILE_CREATION_RULES.md`).

**Last updated:** 2026-08-10

## Layout

- `finished/` — the design's corresponding plan in `plans/finished/` has been executed and reviewed clean. Further split into one `YYYY-MM-DD/` subfolder per date, mirroring `plans/finished/` (see `docs/superpowers/rules/FILE_CREATION_RULES.md`).
- `next/` — the design is approved, but its plan is either still `pending` in `plans/next/`, or no plan has been written yet. Stays flat, no date subfolders.

Every file here says `**Status:** Approved...` at the top — that field is the *design-approval* status (brainstorm sign-off), not implementation status. Finished/next here tracks implementation status, taken from cross-referencing `plans/SUMMARY.md`.

## Files

| Design | Status | Corresponding plan |
|---|---|---|
| `finished/2026-08-03/2026-08-03-doc-audit-and-process-setup-design.md` | finished | `plans/finished/2026-08-03/2026-08-03-doc-audit-and-process-setup.md` |
| `finished/2026-08-03/2026-08-03-work-management-foundation-design.md` | finished | `plans/finished/2026-08-03/2026-08-03-work-management-foundation.md` |
| `finished/2026-08-04/2026-08-04-work-management-projects-edit-delete-view-design.md` | finished | `plans/finished/2026-08-04/2026-08-04-work-management-projects-edit-delete-view.md` |
| `finished/2026-08-04/2026-08-04-work-management-milestone-hierarchy-design.md` | finished | `plans/finished/2026-08-04/2026-08-04-work-management-milestone-hierarchy.md` |
| `finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve-design.md` | finished | `plans/finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve.md` (18/18 tasks, executed 2026-08-08) |
| `next/2026-08-03-platform-users-list-design.md` | pending | none written yet — design-approved 2026-08-03, no plan file exists in `plans/` under either `finished/` or `next/` |
| `next/2026-08-07-work-management-objective-subtree-design.md` | **drift** | its plan, `plans/next/2026-08-07-work-management-objective-subtree.md`, already shipped (git commits `2a77bbc`..`b900ec5`, 2026-08-07) but neither the plan nor this design were ever moved to `finished/` — flagged, not fixed, during the 2026-08-08 pass (out of that session's scope) |
| `next/2026-08-12-objective-viewmodel-owner-fields-fix-design.md` | pending | none written yet — approved 2026-08-12, companion to the frontend repo's `2026-08-12-milestone-tree-mockup-redesign-design.md` |
| `finished/2026-08-08/2026-08-08-work-management-my-project-milestones-design.md` | finished | `plans/finished/2026-08-08/2026-08-08-work-management-my-project-milestones.md` (5/5 tasks, executed 2026-08-08) |
| `finished/2026-08-10/2026-08-10-milestone-ownership-and-subtree-access-design.md` | finished | `plans/finished/2026-08-10/2026-08-10-milestone-ownership-and-subtree-access.md` (2/2 tasks, executed 2026-08-10) |

## Open items

- `platform-users-list-design.md` has been sitting approved-but-unplanned since 2026-08-03 with no corresponding plan file anywhere in `plans/` — worth checking with the user whether it's still wanted, or was superseded/abandoned silently.
- `2026-08-07-work-management-objective-subtree-design.md`/its plan need a finished/next status sync — see the "drift" row above.
- Two loose design files sit directly in `specs/` root, not yet filed into `next/`/`finished/` per the folder/status split this table otherwise follows: `2026-08-06-invite-platform-manager-design.md` and `2026-08-07-legal-entity-logo-upload-design.md`. Checked 2026-08-10: both designs are fully implemented in code already (their plans just have unticked checkboxes) — this is a bookkeeping-only gap, not in-flight work. Not fixed here, out of this session's scope; flagged so it isn't lost again.
