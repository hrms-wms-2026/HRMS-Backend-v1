# Delegate task planning — design

**Date:** 2026-11-11 (review fixes folded in the same day)
**Builds on:** `docs/superpowers/specs/2026-11-10-meeting-delegates-design.md` (branches `feat/meeting-delegates-backend`,
`feat/meeting-delegates-frontend`, not merged yet)

## Why

Meeting delegates (2026-11-10) let a secretary run the CEO's meetings. A secretary also runs the CEO's day: they look at
the CEO's tasks and put time for them on the CEO's planner. Today a delegate sees the principal's calendar read-only
with no Task list, so they cannot do that.

## What the user decided (2026-11-11)

1. **A separate permission.** "Can also plan my tasks" is a checkbox on a delegate grant, off by default. Meeting
   delegation alone does not show the principal's tasks. Only the principal sets it (no HR/admin path — the user
   dropped that).
2. **Task detail.** Clicking one of the principal's tasks opens the **existing task form, read-only** (the user chose
   it over a new small drawer, accepting the extra backend reads below).
3. **Notice to the principal.** Every block a delegate plans, moves/resizes or removes on the principal's planner sends
   the principal a notification, one per write.
4. **No limit** on how many delegates a principal has (unchanged from 2026-11-10).

## Goals

- A principal turns task planning on or off per delegate, when adding them or later.
- A planning delegate, on the principal's calendar, sees the principal's Task list (Focus groups), blocks and editable
  days, and plans, moves, resizes and removes blocks there by the principal's rules.
- The delegate opens any of those tasks read-only in the task form.
- The principal is told in-app about every delegated block write and sees "Planned by <delegate>" on the block.
- Every delegated block write is audited like delegated meeting writes.
- Two people now edit one planner: a stale move/remove is refused, never a silent overwrite.

## Non-goals

- Creating, editing or deleting tasks for the principal, or changing any task field. The delegate writes planner
  blocks only.
- The popover's Work item tab on the principal's calendar (it creates a task): still Meeting only.
- In the read-only task form: comments, attachments, history, subtasks, the monitor load check, every edit control.
- Day view totals, leave layer, My team on the principal's calendar: unchanged (hidden).
- Grouping the notices (left room for it, see Notifications).
- HR/admin setting delegates for someone else.

## Anti-goals

- A meeting-only delegate sees no task title of the principal through any endpoint.
- Without `forEmployeeId`, the five task/project reads below behave exactly as today for everyone.
- `forEmployeeId` opens no write except the three planner block writes; no task or project write accepts it.
- The signed-in identity is never swapped (see Identity).

## Identity (who is acting)

`ICurrentUser` is always the delegate — in logs, audit, `created_by_id`, notices and every write. `forEmployeeId` only
changes **whose data an access check is evaluated against**: the read handlers pass the principal's employee id (and
user id where a check needs one) into their existing access checks, after the delegate check below has passed. No
handler sets or fakes the current user.

## The delegate check — every request, no caching

One service, `IDelegateAccess`:

- `ResolvePlanningSubjectAsync(forEmployeeId, ct)` → the principal's ids when the caller is an **active planning
  delegate** of `forEmployeeId` right now; otherwise a 404 result. "Active planning delegate" = an active delegate (as
  2026-11-10: unrevoked, unexpired, both employees Active/OnLeave) with `can_plan_tasks = 1`.
- It reads the grant from the database on every call. Nothing is cached in the token, session or memory, so a revoke,
  expiry or permission switch-off takes effect on the very next request. The frontend fallback is UX only.

## Model

- `meeting_delegates.can_plan_tasks bit NOT NULL DEFAULT 0` (migration; existing rows false).
- Repository: `IsActivePlanningDelegateAsync(tenantId, principalEmployeeId, delegateEmployeeId, now)`.
- `planned_task_blocks`: unchanged. `created_by_id` (exists) holds who wrote the block; a delegate's block has
  `employee_id = principal`, `created_by_id = delegate`. Who moved it later is in the audit log (below), so no new
  `updated_by_id` column.

## API

### Grant

- `POST /meetings/delegates` body gains `canPlanTasks` (bool, optional, default false).
- New `PATCH /meetings/delegates/{id}` body `{ canPlanTasks: bool }`: only the principal, on an active grant; anything
  else 404 `meeting_delegate_not_found`.
- `MeetingDelegateView` and `MeetingPrincipalView` gain `CanPlanTasks`.

### Planner reads (planning delegate only; anyone else gets the planner's 404)

- `GET /planner/me/range?forEmployeeId=` (exists): `Editable` = the principal's flag for a planning delegate, and
  **false on every day** for a meeting-only delegate.
- `GET /planner/me/focus?forEmployeeId=`: the principal's Focus list.
- `PlannerRangeBlockView` gains `PlannedByName?` (the writer's name when `created_by_id` ≠ the block's employee) and
  `Version` (see Concurrency).

### Task form reads — least privilege

Exactly the reads the read-only task form makes when it opens a task, each accepting `forEmployeeId`:

| Endpoint | Allowed for a planning delegate when |
|---|---|
| `GET /tasks/{id}` | the task is assigned to the principal, **or** it is the parent of a task assigned to the principal (the form shows a subtask's parent) |
| `GET /projects/{id}` | the project has a task currently assigned to the principal |
| `GET /projects/{id}/sprints` | same |
| `GET /projects/{id}/task-statuses`, `GET /projects/{id}/task-categories` | no change: these already answer any signed-in employee of the tenant (no membership check), so the form calls them without `forEmployeeId` |

So three handlers change (task, project, sprints). Each runs its existing checks with the principal as the subject. Everything else — another project of the
principal's, history, subtasks, comments, attachments, milestones, members-only endpoints — stays closed: 404 with
`forEmployeeId`, as today without it. If the plan finds the read-only form needs one more read, it is added to this
table with the same "task/project of the principal's assigned task" rule, or the form section is hidden instead.

### Planner writes (planning delegate only)

- `POST /planner/me/blocks`, `PATCH /planner/me/blocks/{id}`, `DELETE /planner/me/blocks/{id}` accept `forEmployeeId`.
  The planner then runs exactly as for the principal (principal's plan lock, zone, editable days, horizon, overlap with
  the principal's blocks, "task assigned to the principal"), with `created_by_id = delegate` on create. A resize is a
  `PATCH` with a new start/end, like a move. A delegate may move or remove any of the principal's blocks.
- Not a planning delegate: 404, as for a stranger.

## Concurrency

- Each block view carries `Version` = its `UpdatedAt ?? CreatedAt` as ISO-8601 (round-trip "O").
- `PATCH` body and `DELETE` query accept optional `expectedVersion`. When sent and the stored version differs, the write
  is refused: 409 `planner_block_changed`, "This block was changed by someone else. Refresh and try again." The check
  runs under the plan lock, so two writers cannot both pass it.
- Optional, so an older client keeps working; the frontend always sends it. This applies to the principal's own writes
  too (the delegate can be the other writer).

## Notifications

Three in-app events, category `work_management`, in-app only by default like the other orchestrator events:

| Code | Variables | In-app title / body |
|---|---|---|
| `planner_block_planned_for_you` | delegateName, taskTitle, when | "Time planned for you" / "{{delegateName}} planned \"{{taskTitle}}\" for {{when}}." |
| `planner_block_moved_for_you` | delegateName, taskTitle, when | "Planned time moved" / "{{delegateName}} moved \"{{taskTitle}}\" to {{when}}." |
| `planner_block_removed_for_you` | delegateName, taskTitle, when | "Planned time removed" / "{{delegateName}} removed \"{{taskTitle}}\" from {{when}}." |

- `when` is formatted in the principal's planner zone ("Tuesday, November 10, 3:15 PM – 4:15 PM (Asia/Colombo)"),
  never raw UTC.
- Sent after the write commits; the principal's own writes send nothing.
- All three go through one method, `IPlannerDelegateNotifier.BlockWrittenAsync(principal, delegate, action, task,
  start, end)`, so grouping them later is a change in one place.

## Audit

Through `IMeetingDelegateAudit` (actor = delegate, on behalf of the principal, timestamp), the same row shape as
delegated meeting writes:

| Action | Resource | Old / new values |
|---|---|---|
| `meeting.delegate_updated` | meeting_delegate | `canPlanTasks` before / after |
| `planner.block_planned_for_principal` | planned_task_block | — / taskId, startAt, endAt |
| `planner.block_moved_for_principal` | planned_task_block | startAt, endAt before / after (covers resize) |
| `planner.block_removed_for_principal` | planned_task_block | taskId, startAt, endAt / — |

`meeting.delegate_added` records `canPlanTasks`. The principal's own writes are not delegated actions and are not
audited here.

## Frontend

- **Delegates modal:** "Can also plan my tasks" checkbox when adding; each row shows "Meetings" or "Meetings · Tasks"
  and has a switch that PATCHes it.
- **Calendar page, principal view, planning delegate:**
  - the Task list (Focus panel) loads the principal's focus;
  - the principal's editable days are editable (drag from the Task list, block picker, move/resize/remove); every write
    sends `forEmployeeId` and `expectedVersion`;
  - a click on a principal's task (Focus card or block) opens the existing task form with new `readOnly` and
    `actingFor` inputs: its open-time reads send `forEmployeeId`; history, subtasks, comments, attachments and the
    monitor check are hidden and not fetched; every edit control, Save/Request changes, delete, duplicate, convert,
    assign, status move, upload, drafts and the unsaved-changes guard are off;
  - the page does not call `loadProject` for edit purposes in this mode beyond the allowed `GET /projects/{id}`;
  - the create popover stays Meeting only; Day totals, leave, My team stay hidden.
- **Meeting-only delegate:** unchanged from 2026-11-10.
- **Blocks:** "Planned by <name>" on a block with `plannedByName`, for the principal and the delegate.
- **409 `planner_block_changed`:** "This block was changed by someone else." and the range reloads.
- **Permission lost mid-session** (404 on a write or focus read): reload principals; fall back to the meeting-only view
  or "My calendar", and say "You can no longer plan for <name>."

## Tests that matter

- Meeting-only delegate: 404 for focus, each task-form read and each block write; `Editable=false` on every range day.
- Planning delegate: a block lands on the principal with `created_by_id = delegate` under the principal's rules (past
  day, beyond horizon, overlap, task not assigned to the principal → the principal's errors).
- Switching the permission off, revoking, or expiry makes the very next read and write 404 (no caching).
- Each task-form read: unchanged without `forEmployeeId`; 404 for a stranger, a meeting-only delegate, a task not
  assigned to the principal, a project without one; the principal's view otherwise; a subtask's parent readable.
- History, subtasks, comments with `forEmployeeId`: 404 (closed).
- `ICurrentUser` is the delegate in every delegated write (audit actor, `created_by_id`, notice "delegateName").
- Stale `expectedVersion` → 409 for move and remove, for both the principal and the delegate.
- One notice per delegated write with `when` in the principal's zone; none for the principal's own writes.
- The audit rows above, with old values on move/resize.
- The read-only task form makes no write call and shows no edit control; its reads are exactly the five above.
- Parity tests: `SchemaParityTests` already allows `meeting_delegates`; `ReferenceDataParityTests` allows the three new
  notification templates as added after the PostgreSQL move.

## Calls made in this design (review them)

- Least privilege for the task-form reads: only the five reads the form needs, only for the principal's assigned task
  (or its parent) and that task's project.
- `forEmployeeId` as a query parameter (as on the calendar and planner reads), not a header.
- One notice per write; one notifier method so grouping is a later one-place change.
- A planning delegate may move or remove blocks the principal made.
- Optimistic concurrency by `UpdatedAt ?? CreatedAt` version, optional on the wire, applied to everyone's moves/removes.
