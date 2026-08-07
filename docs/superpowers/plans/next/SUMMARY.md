# plans/next/ — Summary

**Purpose:** Everything in `plans/` that is not finished yet. Two kinds of content live here side by side:
1. Plans that have been written and started but whose implementation isn't done (status: `pending`).
2. Raw context for features flagged during other work as "not now, but don't lose this" — not designs or plans yet (those go through `superpowers:brainstorming` → `docs/superpowers/specs/` → a plan file here when actually started).

This folder absorbed the former top-level `docs/superpowers/next-plan/` folder on 2026-08-06 as part of the `plans/` restructure into `finished/` and `next/` — see `FILE_CREATION_RULES.md` in `docs/superpowers/rules/`.

**Last updated:** 2026-08-06

## Files

- `2026-08-06-work-management-milestone-membership-and-achieve.md` — status: **pending** (0/17 tasks). Closes three gaps parked at the end of the milestone-hierarchy plan's final review, plus a new Achieve completion workflow for Projects/Objectives. Built from `specs/2026-08-06-work-management-milestone-membership-and-achieve-design.md`. Move to `plans/finished/` and flip status to `finished` in `plans/SUMMARY.md` once all 17 tasks are done and the final review is clean.
- `Project Management.md` — two future features, both raw context, not designs:
  1. Milestone (Objective) In-Charge Role & Permission System — **superseded**, see the file's own header: fully brainstormed and designed in `specs/2026-08-04-work-management-milestone-hierarchy-design.md`.
  2. Project Lifecycle Workflow, Approval Pipeline, Archive/Restore, and Progress Calculation — not started. Backend-relevant subset of a manager corrections doc on the Project Management user journey (2026-08-04). Blocked on Objective/Task CRUD existing first; conflicts with the schema's current "forbidden: free-form status" note on `projects`.

## Known drift

- The previous version of this summary (when the folder was `docs/superpowers/next-plan/`) also listed a `Notification Management (Outbox Mapping).md` file. That file does not exist anywhere in the repo — the summary entry was never backed by an actual file. If that context still matters, it needs to be re-captured from scratch; nothing was lost in this move, the source file was already missing before 2026-08-06.

## Open items

- Neither remaining raw-context item above is brainstormed yet. See each one's "Suggested next step" section in `Project Management.md`.
- `2026-08-06-work-management-milestone-membership-and-achieve.md` is next up for execution — see `plans/SUMMARY.md` for the full plan list and status.
