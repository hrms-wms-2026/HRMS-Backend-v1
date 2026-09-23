# Task Subtasks — Design

**Status:** Approved 2026-09-21 (chat brainstorm). Companion: `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-09-21-task-subtasks-design.md` (same document).

**Status (implementation):** backend plan finished 2026-09-21; frontend companion remains tracked in the frontend repository.

Repos touched: `HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`

## Problem

The Work module's Task Board/Backlog has no concept of subtasks. Users want
to break a task into smaller pieces of work, each assignable to a different
person, similar to ClickUp: an "Add subtask" affordance inside a task, a
progress badge on the parent (e.g. `0/1`), and the ability to see subtasks
without leaving the Board/Backlog.

The screenshots that prompted this request are ClickUp's own UI (Zen Ti's
Workspace / Team Space / Project 2), used purely as a visual reference — not
existing onevoNew UI. Confirmed by grepping the frontend for "subtask" /
"Subtasks" toggle: zero matches anywhere in the codebase.

## Existing groundwork

- `WorkTask.cs` (`ONEVO.Domain/Features/WorkManagement/Tasks/Entities`)
  already has a `Guid? ParentTaskId` column, but `WorkTaskConfiguration.cs`
  configures no FK/relationship for it, and no Application-layer code reads
  or writes it. It is a dead column today.
- Assignment is already modeled via `TaskAssignment` (join table:
  `TaskId, UserId, EmployeeId, AssignedById, AssignedAt`) with existing
  `POST tasks/{id}/assignments` / `DELETE tasks/{id}/assignments/{employeeId}`
  endpoints.
- `task-form-modal.component.ts` is the single shared create/edit surface for
  a task (per prior work: "Description editor unified" — see
  `2026-09-15` decisions in project memory).
- Board card: `work/ui/task-card/task-card.component.ts` (single-assignee
  avatar popover, priority badge, due date — no expand/collapse affordance
  today).
- Backlog: `work/ui/task-table/task-table.component.ts` — flat `<table>`,
  one `<tr>` per task, no grouping/tree.
- Reusable pickers: `app-employee-picker` (inputs: `open`, `inline`; outputs:
  `selected`, `closed`) and `app-employee-avatar` (input: `employeeId`,
  `showName`, `invertText`) — both reusable as-is for a subtask's assignee.
- Indentation reference pattern: `work/ui/task-tree-row/task-tree-row.component.ts`
  (Milestone tree feature — has a `depth` input driving `paddingLeft`; not
  wired to Board/Backlog, but a useful pattern to mirror).

## Decisions (confirmed with user)

1. **Data model**: a subtask is a full `WorkTask` row with `ParentTaskId`
   set — not a separate lightweight entity. Reuses all existing status,
   priority, assignment, and board-query infrastructure.
2. **Visibility**: subtasks are visible both in the task detail panel
   (add/edit/assign) AND as nested rows/cards under their parent on the
   Board and Backlog — not as independent top-level cards in a column.
3. **Subtask creation fields**: reduced set — title, single assignee,
   due date, priority. (A subtask can later be opened and given full fields
   through the normal task edit flow, since it's a real `WorkTask` row.)

## Out of scope (v1)

- Drag-and-drop of a subtask between status columns.
- Subtask-level checklists.
- Multi-level nesting (a subtask having its own subtasks) — `ParentTaskId`
  technically allows it, but v1 UI and queries assume exactly one level.
- Bulk "convert existing task into a subtask of another task."
- Changing a subtask's parent after creation.

None of this is precluded later — the data model (`ParentTaskId` on a
first-class `WorkTask`) already supports it.

## Backend design

### Schema

- `WorkTaskConfiguration.cs`: configure `ParentTaskId` as a real
  self-referencing FK (`HasOne().WithMany().OnDelete(Restrict)` — a task
  with subtasks cannot be hard-deleted without first deleting/reassigning
  its subtasks, consistent with existing FK behavior for `StatusId` etc.).
  Add an index on `(TenantId, ParentTaskId)`.
- New EF migration for the FK + index (the column itself already exists).

### API surface

- `POST tasks/{parentTaskId}/subtasks` — new endpoint + command
  (`CreateSubtaskCommand`/Handler, mirroring `CreateTaskCommandHandler`'s
  validation shape but with the reduced field set: `Title`, `Priority?`,
  `DueDate?`, `AssigneeEmployeeId?`). The handler loads the parent task,
  copies `ObjectiveId`, `ProjectId`, `CategoryId` from it, and defaults
  `StatusId` to the project's first/default task status — so the reduced
  create form never needs to ask for fields the underlying `WorkTask`
  schema requires. Permission check mirrors `CreateTaskCommandHandler`
  (effective-manager on the objective).
- `GET tasks/{parentTaskId}/subtasks` — new query, returns full
  `WorkTaskResponse[]` for the parent's direct children (one level only).
  Used for lazy-loading when a Board card or Backlog row is expanded.
- `WorkTaskResponse` gains three fields: `ParentTaskId?`,
  `SubtaskTotalCount`, `SubtaskCompletedCount`. These two counts are
  populated for every task returned by the Board/Backlog query (see below)
  so the "0/1"-style badge can render without a per-card extra call.
- `GetProjectTasksQuery` (Board/Backlog source): add `WHERE ParentTaskId IS
  NULL` to the main projection (subtasks never appear as independent
  top-level cards), and compute `SubtaskTotalCount` /
  `SubtaskCompletedCount` per parent via a single grouped sub-query
  (`GROUP BY ParentTaskId`) joined into the projection — not N+1 per task.
  "Completed" for the count means the subtask's `StatusId` maps to the
  project's terminal/"done" status, same notion the Board already uses for
  column grouping.
- Assigning/unassigning a subtask's person reuses the existing
  `POST tasks/{id}/assignments` / `DELETE tasks/{id}/assignments/{employeeId}`
  endpoints unchanged (a subtask is a `WorkTask`, so `{id}` is just its id).

### Testing

- Unit tests for `CreateSubtaskCommandHandler`: field defaulting from
  parent, permission check, rejection when parent task doesn't exist or
  belongs to a different project/tenant.
- Unit tests for the updated `GetProjectTasksQuery` projection: counts are
  correct, subtasks excluded from the top-level list, tenant isolation
  (RLS) holds for the grouped sub-query.
- Integration test: create task → create subtask → assign subtask →
  fetch board → verify count fields → fetch `GET .../subtasks` → verify
  full row.

## Frontend design

### Model & API client

- `task.model.ts`: add `parentTaskId?: string`, `subtaskTotalCount: number`,
  `subtaskCompletedCount: number` to `WorkTask`.
- `task-api.service.ts`: add `createSubtask(parentTaskId, request)` and
  `getSubtasks(parentTaskId)`.
- `task-board.store.ts`: add a `subtasksByParentId` map (populated lazily
  on expand, not on initial board load) plus `loadSubtasks(parentTaskId)`
  and `createSubtask(parentTaskId, request)` methods. `createSubtask`
  optimistically increments the parent's `subtaskTotalCount` in
  `store.tasks()` so the badge updates without a full board reload.

### Task detail panel (`task-form-modal.component.ts`)

New "Subtasks" section, shown only in edit mode (a task must be persisted
before it can own children):

- List of existing subtasks: title, `app-employee-avatar` for the assignee,
  priority chip, a done/not-done toggle (patches the subtask's status via
  the existing `PATCH tasks/{id}/status` endpoint).
- Inline "+ Add subtask" row: title input, `app-employee-picker` in
  `inline` mode for the assignee, a priority dropdown, a due-date picker,
  Save/Cancel — mirroring the interaction shape from the reference
  screenshots, built entirely from existing form primitives already used
  elsewhere in the same component.

### Board (`task-card.component.ts`)

- When `subtaskTotalCount > 0`, render a disclosure chevron and a
  `completed/total` badge (e.g. `0/1`) on the card footer, next to the
  existing due-date/hours row.
- Expanding calls `store.loadSubtasks(taskId)` (lazy — only when the user
  actually expands) and renders compact child rows indented beneath the
  parent, inside the same card: title, assignee avatar, status-color dot.
  Subtasks are not separate cards in the column and do not participate in
  column drag-and-drop.
- Clicking a child row opens it in the same task detail panel as any other
  task (it's a normal `WorkTask`, so the existing open-task flow just
  works).

### Backlog (`task-table.component.ts`)

- Same chevron + lazy-expand behavior as the Board card, applied per `<tr>`.
- Expanded children render as additional indented `<tr>`s directly below
  the parent row, using the padding-by-depth pattern already established in
  `task-tree-row.component.ts` (that component itself is not reused, since
  it belongs to the Milestone tree feature and has different data needs —
  only its indentation convention is mirrored here).

### Testing

- Vitest specs for `task-board.store.ts`: `loadSubtasks` populates the map
  correctly; `createSubtask` optimistic count update, and rollback on API
  failure (mirroring the existing `moveTask` optimistic-update pattern).
- Component tests: task card renders badge only when count > 0; expand
  triggers exactly one `loadSubtasks` call and is idempotent on repeat
  toggles; backlog row indentation renders at the expected depth.
- Manual verification in the browser preview: create a task, add two
  subtasks with different assignees, verify the badge, expand/collapse on
  both Board and Backlog, open a subtask from the nested row.

## Migration / rollout notes

- The `ParentTaskId` FK migration is additive (index + constraint on an
  already-existing nullable column) — no backfill needed, existing rows
  all have `ParentTaskId = NULL`.
- No feature flag: this is purely additive UI (badge/chevron only appears
  when a task actually has subtasks) and an additive API surface, so it is
  safe to ship directly.
