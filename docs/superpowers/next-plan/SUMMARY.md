# next-plan/ — Summary

**Purpose:** Raw context for features flagged during other work as "not now, but don't lose this" — captured so a future session can pick them up without re-deriving them from a live conversation. Not designs or plans yet (those still go through `superpowers:brainstorming` → `docs/superpowers/specs/` → `docs/superpowers/plans/` when actually started).

**Last updated:** 2026-08-04

## Files

- `Project Management.md` — two future features, both raw context, not designs:
  1. Milestone (Objective) In-Charge Role & Permission System. A tree-structured ownership/reporting model for Objectives (Default Objective → sub-milestones), each with a Milestone-in-charge, automatic reporting-manager chaining, parent/child deadline-hours constraints, and project/milestone-scoped capabilities (view/create/edit/delete milestone, create task, etc.) distinct from the existing tenant-wide `projects:*` RBAC permissions. Captured out of scope from the 2026-08-04 Work Management Edit/Delete/Get/List Project brainstorm — needs a schema change (`project_members` currently forbids a `role` column) and depends on Objective/Task CRUD existing first.
  2. Project Lifecycle Workflow, Approval Pipeline, Archive/Restore, and Progress Calculation. Backend-relevant subset of a manager corrections doc on the Project Management user journey (2026-08-04): workflow status separate from schedule health, an approval pipeline for creation and baseline changes, archive-with-restore replacing permanent delete, and milestone/task-weighted progress calculation. Conflicts with the schema's current "forbidden: free-form status" note on `projects` and shares the Objective/Task CRUD dependency with item 1. The UI/UX-only subset of the same feedback lives in the frontend repo's `docs/superpowers/next-plan/Project Management.md`.

## Open items

- Neither feature above is brainstormed yet. See each one's "Suggested next step" section in `Project Management.md`.
