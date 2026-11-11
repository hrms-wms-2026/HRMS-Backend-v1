# Delegate task planning — design

**Date:** 2026-11-11
**Builds on:** `docs/superpowers/specs/2026-11-10-meeting-delegates-design.md` (branches `feat/meeting-delegates-backend`, `feat/meeting-delegates-frontend`, not merged yet)

## Why

Meeting delegates (2026-11-10) let a secretary run the CEO's meetings. A secretary also runs the CEO's day: they look at
the CEO's tasks and put time for them on the CEO's planner. Today a delegate sees the principal's calendar read-only
with no Task list, so they cannot do that.

## What the user decided (2026-11-11)

1. **A separate permission.** "Can also plan my tasks" is a checkbox on a delegate grant, off by default. Meeting
   delegation alone does not show the principal's tasks.
2. **Task detail.** Clicking one of the principal's tasks opens a read-only drawer with its details.
3. **Notice to the principal.** When a delegate plans, moves or removes a block on the principal's planner, the
   principal gets a notification.

## Goals

- A principal can turn task planning on or off per delegate, when adding them or later.
- A planning delegate, on the principal's calendar, sees the principal's Task list (Focus groups), the principal's
  blocks and the principal's editable days, and can plan, move, resize and remove blocks there, by the same rules the
  principal has.
- The delegate can open any of those tasks read-only.
- The principal is told, in-app, about every block a delegate writes, and sees "Planned by <delegate>" on it.
- Every delegated block write is audited like delegated meeting writes.

## Non-goals

- Creating, editing or deleting tasks for the principal, or changing any task field (status, estimate, due date,
  assignee, description). The delegate writes planner blocks only.
- The popover's Work item tab on the principal's calendar (it creates a task): still Meeting only.
- Comments, attachments, time logs in the read-only drawer.
- The principal's Day view totals, leave layer and My team on the principal's calendar: unchanged (hidden).
- Batching or digesting the notices.

## Anti-goals

- A meeting-only delegate must not see one task title of the principal through any endpoint.
- The task read for a delegate must not loosen the normal task detail endpoint (`GET /tasks/{id}`) for anyone.

## Model

- `meeting_delegates.can_plan_tasks bit NOT NULL DEFAULT 0` (migration, existing rows false).
- "Active planning delegate" = an active delegate (as today: unrevoked, unexpired, both employees Active/OnLeave) with
  `can_plan_tasks = 1`. Repository: `IsActivePlanningDelegateAsync(tenantId, principalEmployeeId, delegateEmployeeId, now)`.
- `planned_task_blocks.created_by_id` already exists and holds the employee who wrote the block. A delegate's block keeps
  `employee_id = principal`, `created_by_id = delegate`.

## API

### Grant

- `POST /meetings/delegates` body gains `canPlanTasks` (bool, optional, default false).
- New `PATCH /meetings/delegates/{id}` body `{ canPlanTasks: bool }`: the principal changes it on an active grant.
  404 `meeting_delegate_not_found` for another person's or a revoked grant.
- `MeetingDelegateView` and `MeetingPrincipalView` gain `CanPlanTasks`.

### Reads (planning delegate only; anyone else gets the planner's 404)

- `GET /planner/me/range?forEmployeeId=` (exists): `Editable` is the principal's editable flag for a planning delegate,
  and **false on every day** for a meeting-only delegate (today it is computed for the principal regardless).
- `GET /planner/me/focus?forEmployeeId=`: the principal's Focus list.
- New `GET /planner/me/tasks/{taskId}?forEmployeeId=`: a read-only view of one task currently assigned to the
  principal: id, short id, title, project name, module title, category (name, kind, icon), status name, priority, due
  date, estimate minutes, logged minutes, description (HTML as stored), assignee names. A task not assigned to the
  principal is 404. Without `forEmployeeId` it is 404 (the caller uses the normal task endpoint for their own).
- `PlannerRangeBlockView` gains `PlannedByName?`: the writer's name when `created_by_id` differs from the block's
  employee; null otherwise. Shown to the principal and to a planning delegate.

### Writes (planning delegate only)

- `POST /planner/me/blocks`, `PATCH /planner/me/blocks/{id}`, `DELETE /planner/me/blocks/{id}` accept `forEmployeeId`.
  With a principal, the planner runs exactly as it does for the principal (principal's plan lock, zone, editable days,
  horizon, overlap with the principal's blocks, "task assigned to the principal"), and writes `created_by_id = delegate`
  on create. A move or remove of any of the principal's blocks is allowed (also ones the principal made).
- Not a planning delegate (meeting-only, revoked, expired, permission turned off): 404, as for a stranger.

## Notifications

Three in-app events, category `work_management`, in-app only by default like the other orchestrator events:

| Code | Variables | In-app title / body |
|---|---|---|
| `planner_block_planned_for_you` | delegateName, taskTitle, when | "Time planned for you" / "{{delegateName}} planned \"{{taskTitle}}\" on {{when}}." |
| `planner_block_moved_for_you` | delegateName, taskTitle, when | "Planned time moved" / "{{delegateName}} moved \"{{taskTitle}}\" to {{when}}." |
| `planner_block_removed_for_you` | delegateName, taskTitle, when | "Planned time removed" / "{{delegateName}} removed \"{{taskTitle}}\" from {{when}}." |

`when` is formatted in the principal's planner zone ("Tuesday, November 10 at 3:15 PM – 4:15 PM"), never raw UTC (the
2026-11-09 reminder lesson). Sent after the write commits; the principal's own writes send nothing.

## Audit

Through `IMeetingDelegateAudit`, the same row shape as delegated meeting writes (actor = delegate, on behalf of the
principal):

| Action | Resource | Values |
|---|---|---|
| `meeting.delegate_updated` | meeting_delegate | old/new `canPlanTasks` |
| `planner.block_planned_for_principal` | planned_task_block | taskId, startAt, endAt |
| `planner.block_moved_for_principal` | planned_task_block | old and new startAt/endAt |
| `planner.block_removed_for_principal` | planned_task_block | taskId, startAt, endAt |

`meeting.delegate_added` records `canPlanTasks`.

## Frontend

- **Delegates modal:** a "Can also plan my tasks" checkbox when adding; each row shows "Meetings" or "Meetings · Tasks"
  and has a switch that PATCHes it.
- **Calendar page, principal view, planning delegate:**
  - the Task list (Focus panel) loads the principal's focus;
  - the principal's editable days are editable (drag from the Task list, block picker, move/resize/remove a block), every
    write sends `forEmployeeId`;
  - a click on a principal's task (Focus card or block) opens the new read-only task drawer, not the task form;
  - the create popover stays Meeting only; Day totals, leave, My team stay hidden.
- **Meeting-only delegate:** unchanged from 2026-11-10 (read-only, no Task list).
- **Blocks:** "Planned by <name>" on a block whose `plannedByName` is set, for the principal and the delegate.
- **Permission lost mid-session** (a 404 on a write or focus read): the page reloads principals; if the person is gone or
  no longer plans, it falls back to the meeting-only view or "My calendar", and says "You can no longer plan for <name>."

## Tests that matter

- A meeting-only delegate gets 404 for focus, task read and every block write, and `Editable=false` on every range day.
- A planning delegate's block lands on the principal with `created_by_id = delegate`, under the principal's rules
  (past day, beyond horizon, overlap, task not assigned to the principal: the principal's errors).
- Turning the permission off makes the next write 404.
- The principal gets exactly one notice per delegated write, with `when` in their zone; none for their own writes.
- The audit rows above, with old values on move.
- `GET /tasks/{id}` behaviour is unchanged.
- Parity tests: `SchemaParityTests` already allows `meeting_delegates`; `ReferenceDataParityTests` must allow the three
  new notification templates as added after the PostgreSQL move (same idea as `AddedTables`).

## My calls (check these)

- The read-only drawer is a new small component on a new endpoint, not the task form in a view mode: the task form is
  tied to project access and edit flows, and loosening its endpoint would break the anti-goal.
- One notice per write (no batching). If a secretary plans ten blocks, the CEO gets ten notices.
- A planning delegate may move or remove blocks the principal made, not only their own.
- The drawer shows only fields listed above (no comments, attachments, history).
