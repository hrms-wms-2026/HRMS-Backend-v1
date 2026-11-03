# ClickUp-style Task Status Groups Design

## Context

Task statuses today (`ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus`) are a flat,
per-project list — e.g. To Do / In Process / Review / Done — each with a `Visibility`
(`public`/`private`) and a `MarksTaskComplete` flag. The existing "Edit task statuses" screen
(`board-structure-editor.component.ts`) renders them as a flat draggable list with a Private
checkbox and a Marks-complete radio per row.

The user wants a ClickUp-style presentation and workflow instead:
- Statuses grouped under three fixed headers: **Not Started / Active / Done**.
- A status can carry a **color**, chosen with the same preset-swatch + custom-color-input pattern
  already used in project creation (`project-form-modal.component.ts`), and the task board's
  column headers should render that color.
- Clocking in on a task that is still in a Not Started status should not be allowed to stay there —
  the system auto-moves it into an Active status and tells the user it did so.
- The existing "Private" status concept (only a milestone/objective owner can move a task into it)
  carries forward into the new grouped UI as a lock-icon toggle instead of a checkbox.

## Current State (verified against code, not assumed)

- `TaskStatus` columns: `ProjectId`, `ObjectiveId` (nullable — see below), `Name`, `DisplayOrder`,
  `RequiresApproval`, `ApproverId`, `MarksTaskComplete`, `Visibility`. No `Color`, no group/category.
- `ObjectiveId == null` is documented as "the Project-level template"; `ObjectiveId` set is meant to
  be an objective's own independently-customizable copy. **In practice this second path is dead
  code** — `GetProjectTaskStatusesQueryHandler`, `CreateTaskStatusCommandHandler`,
  `ReorderTaskStatusesCommandHandler`, and the board/editor UI all exclusively read/write
  `ITaskStatusRepository.GetProjectTemplateAsync` (`ObjectiveId == null` rows only). No command ever
  creates an `ObjectiveId`-scoped row. This design does not touch that path; it remains unused.
- `ReorderTaskStatusesCommandHandler` already enforces **"exactly one complete status, always"**
  (lines 64-65, 84-85) and `MoveTaskStatusCommandHandler` already enforces the private-status
  permission: a plain active milestone member cannot move a task into a `Visibility == private`
  status; only an "effective manager" (the objective owner, or an owner of an ancestor objective —
  `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync`) can.
- `ClockInTaskCommandHandler` performs zero status checks today — it validates assignee, non-100%
  progress, and no existing open session, and nothing else.
- `CreateTaskCommandHandler` assigns a new task's initial status via
  `statuses.Where(s => !s.MarksTaskComplete).OrderBy(s => s.DisplayOrder).FirstOrDefault()`.
- Project creation's color picker (`project-form-modal.component.ts:206-254,768-773`) is a preset
  swatch grid (`#2563eb`/`#16a34a`/`#7c3aed`/`#ea580c`) plus a "Custom" toggle revealing a native
  `<input type="color">`. It is inline, not a reusable component.
- `task-board-column.component.ts` renders only the status name and task count — no color.

## Data Model Changes

Add to `TaskStatus`:
- `Category` (string constant, same pattern as `TaskStatusVisibilities`): `not_started` | `active` | `done`.
- `Color` (string, hex `#RRGGBB`).

`MarksTaskComplete` remains a persisted column (avoids touching every existing consumer of that
flag) but becomes **fully derived, single-writer, server-side only**: `true` iff `Category == done`.
It is removed from `CreateTaskStatusCommand`, `EditTaskStatusCommand`, and
`ReorderTaskStatusesCommand`'s request DTOs — no client ever sets it directly.

Because the codebase already enforces "exactly one complete status", and `Category == done` now
drives that flag, **the Done group is capped at exactly one row** (rename/recolor/toggle-private
only; not addable, not deletable). This is a deliberate scope-narrowing decision: it keeps every
existing single-complete-status consumer correct without a rewrite, at the cost of not supporting
ClickUp's multi-status "Complete" group. Not Started and Active each hold 1+ rows freely.

## Migration

Two-step: add `Category` and `Color` as nullable, backfill via SQL, then alter both to `NOT NULL`
in the same migration file.

Backfill heuristic per `(TenantId, ProjectId, ObjectiveId)` scope:
1. The row with `MarksTaskComplete = true` → `Category = done`, `Color = #16A34A`. If no row in a
   scope has `MarksTaskComplete = true` (shouldn't happen given the existing invariant, but the
   invariant is enforced by `ReorderTaskStatuses`, not by a DB constraint, so historical rows might
   predate it) → fall back to the row with `MAX(DisplayOrder)` in that scope.
2. Of the remaining rows, the one with `MIN(DisplayOrder)` → `Category = not_started`, `Color = #94A3B8`.
3. All other remaining rows → `Category = active`, `Color = #2563EB`.

**Repair pass (required, not optional):** after the above, for any `(TenantId, ProjectId, ObjectiveId)`
scope left with zero `active`-category rows — e.g. a project that only ever had 2 statuses — insert
a synthetic `"In Progress"` row, `Category = active`, `Color = #2563EB`, `Visibility = public`,
`DisplayOrder = MAX(DisplayOrder) + 1` for that scope. Without this, every clock-in on that project
would hit clock-in's "no Active status found" error permanently. This must be verified against a
copy of real data (row counts per scope) before shipping, not just reasoned about.

Update `DefaultTaskStatusTemplate.BuildRows`: To Do → `not_started`/`#94A3B8`,
In Process → `active`/`#2563EB`, Review → `active`/`#7C3AED`,
Done → `done`/`#16A34A` (keeps existing `MarksTaskComplete = true`, `Visibility = private`).

## Backend Behavior Changes

**CreateTaskStatusCommand**: add required `Category`. Validator: must be one of the three constants.
Handler derives `MarksTaskComplete = (Category == done)`. Reject with a conflict if
`Category == done` and the scope already has a Done row ("This project already has a Done status").

**EditTaskStatusCommand**: allow changing `Name`/`Category`/`Color`/`Visibility`. Reject changing
category to `done` if a different Done row already exists. Reject changing the current sole Done
row's category away from `done` ("A project must always have exactly one Done status").

**DeleteTaskStatusCommand**: add guards — reject deleting the last remaining `active`-category row
("A project must always have at least one Active status") and reject deleting the sole Done row.
`not_started` has no minimum-count guard.

**ReorderTaskStatusesCommand**: `Update` DTO gains `Category` and `Color`; `MarksTaskComplete` is
removed from the wire payload (derived server-side from `Category`, closing the two-writer risk).
Existing "exactly one complete" check is restated as "exactly one `Category == done`"; add "at least
one `Category == active`".

**CreateTaskCommandHandler**: default status selection becomes "first `not_started` status by
`DisplayOrder`, falling back to first `active` status by `DisplayOrder` if none" — same real-world
behavior as today's "first non-complete status" once groups are populated correctly, but explicit.

**ClockInTaskCommandHandler**: after the three existing validations, load the task's current
`TaskStatus`. If `Category == not_started`:
- Find the first `Category == active AND Visibility == public` status for the project template,
  ordered by `DisplayOrder`.
- If found: inside the existing transaction, set `task.StatusId` to it, write a
  `TaskStatusChangeLog` row (same shape `MoveTaskStatusCommandHandler` writes, for a consistent task
  history), and include the moved-to status (`Id`, `Name`, `Color`) in the response.
- If none found (every Active status is private): return `Result.Conflict` with a message telling
  the user to ask the module owner to move the task first. **Do not bypass the existing
  private-status permission check to force a move** — that would let clock-in silently defeat a
  permission this codebase already enforces intentionally.

This changes `ClockInTaskCommand`'s response from `Result` to `Result<ClockInTaskResponse>` carrying
an optional `MovedToStatus`. Update the controller route and `task-api.service.ts`'s `clockIn()`
call accordingly.

## Frontend Changes

- Extract the inline color picker out of `project-form-modal.component.ts` into a shared standalone
  component (presets + selected value + change output). Refactor `project-form-modal` to consume it
  (no behavior change there); reuse it in the new status editor.
- Replace `board-structure-editor.component.ts`'s flat list with three fixed group sections
  (Not Started / Active / Done headers, each showing a count and an add button — Done's add button
  hidden since it is capped at one row). Use `cdkDropListGroup` so rows can be dragged between
  sections (a cross-group drop updates that row's `Category`), plus within-group reordering as
  today. Each row: colored dot (from `Color`), inline-editable name, a lock-icon toggle
  (`Visibility`) replacing the current checkbox, and a `...` menu for delete (disabled on the sole
  Done row and on the last remaining Active row, mirroring the backend guards).
- `TaskStatusColumn` model and the task-status API/store layer gain `category`/`color` fields.
- `task-board-column.component.ts` header renders a small status indicator before the name — open
  ring for Not Started, filled dot for Active, checkmark circle for Done — tinted with
  `status().color`.
- Wherever the clock-in call is made (task detail / board), if the response includes
  `movedToStatus`, show a toast ("Moved to '<name>' to start tracking time") via the existing
  notification/toast service and update the task's status locally (or refetch) so the board
  reflects the move immediately.

## Out of Scope

- The "Status template" dropdown / "Inherit from Space" toggle from the ClickUp reference
  screenshot — there is no "Space" concept in this codebase to inherit from.
- Building a real UI for the `ObjectiveId`-scoped per-objective status override path. It stays
  schema-only and unused, as it is today.
- Any change to who may edit the status list itself (project-settings-page authorization is
  unchanged) — only the private-status *move* permission and its UI surfacing are addressed here.

## Testing Plan

Backend (xUnit): Create/Edit/Delete guard tests (Done-cap, Active-minimum, no-second-Done),
`CreateTaskCommandHandler`'s new not_started-then-active default-status fallback,
`ClockInTaskCommandHandler`'s auto-move happy path, the all-Active-private conflict path, and the
`ReorderTaskStatuses` category/color plumbing with `MarksTaskComplete` no longer accepted from the
client. Migration backfill/repair logic verified against fixture data covering: a project with only
2 statuses (no Active) and a project with a historical row missing `MarksTaskComplete = true`
entirely.

Frontend (Vitest): grouped editor rendering, cross-group drag updating category, add/delete guards
disabled per group correctly, color picker interaction, `task-board-column` color rendering, and the
clock-in toast wiring. Manual verification via the Angular dev server preview once implemented.
