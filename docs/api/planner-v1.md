# Planner API v1.1

Designs:
- `docs/superpowers/specs/2026-10-26-meeting-hours-and-day-planner-design.md` (Part B, the day planner).
- `docs/superpowers/specs/next/2026-10-27-planner-calendar-merge-design.md` (v1.1: Planner = Calendar).

Web only. An employee plans **today and up to 28 days ahead**, in their own timezone (the zone `GET /api/v1/calendar/my-timezone` returns). Past days are read-only.

Feature flag `Planner:Enabled` (default off): while it is off, every endpoint answers **503 `planner_disabled`**.

## Endpoints

| Method | Route | Who | Body |
|---|---|---|---|
| GET | `/api/v1/planner/me?date=YYYY-MM-DD` | the caller (date defaults to today; up to today + 28) | — |
| GET | `/api/v1/planner/me/range?from=YYYY-MM-DD&to=YYYY-MM-DD` | the caller (at most 42 days) | — |
| GET | `/api/v1/planner/me/focus` | the caller | — |
| GET | `/api/v1/planner/employees/{employeeId}?date=` | the employee, or any manager in their reporting-line ancestor chain; anyone else 404 | — |
| POST | `/api/v1/planner/me/blocks` | the caller | `{ taskId, startAt, endAt }` |
| PATCH | `/api/v1/planner/me/blocks/{blockId}` | the block's owner | `{ startAt, endAt }` (may move to another editable day) |
| DELETE | `/api/v1/planner/me/blocks/{blockId}` | the block's owner | — |

The day reads and the writes return a day (`PlannerDayResponse`). A write returns **the day of the block it wrote**; a move to another day returns the target day.

```json
{ "date": "2026-11-02", "timezone": "Asia/Colombo", "editable": true,
  "blocks":   [ { "id": "…", "taskId": "…", "taskTitle": "Write the report", "startAt": "…", "endAt": "…",
                  "plannedMinutes": 120, "actualMinutes": 45, "stillAssigned": true, "overlapsMeeting": false } ],
  "meetings": [ { "taskId": "…", "seriesId": "…", "originalStart": "…", "startAt": "…", "endAt": "…", "title": "Weekly sync",
                  "isPrivate": false, "cancelled": false, "declined": false, "plannedMinutes": 60, "actualMinutes": 60 } ],
  "dueTasks": [ { "taskId": "…", "title": "…", "dueDate": "2026-11-02", "estimatedHours": 5 } ],
  "totals":   { "plannedMinutes": 180, "actualMinutes": 105 }, "attendanceMinutes": 450 }
```

`dueTasks` (due today or overdue) is kept for compatibility; the merged Planner page uses `/me/focus` instead.

### Range (`/me/range`)

The calendar grid's days, one entry per date from `from` to `to`:

- `blocks` carry `projectId` (to open the task on its board) and the day's `actualMinutes`.
- `blockMinutes` is the day's planned task minutes. The load the grid shows (blocks plus busy calendar items, overlap counted once) is computed by the client.
- `workWindow` is the legal entity's working hours read as wall-clock in the employee's zone.
  - The break is centred and rounded down to 15 minutes.
  - `workingMinutes` is 0 on a non-working day.
  - It is `null` when the schedule is unset or overnight, the same rule clock-in uses.
- Days after the horizon come back with `editable: false`.

```json
{ "timezone": "Asia/Colombo", "today": "2026-11-02",
  "days": [ { "date": "2026-11-02", "editable": true,
              "workWindow": { "workStart": "09:00:00", "workEnd": "17:00:00", "breakMinutes": 60, "breakStart": "12:30:00",
                              "isWorkingDay": true, "workingMinutes": 420 },
              "blocks": [ { "id": "…", "taskId": "…", "projectId": "…", "projectName": "Portal", "taskTitle": "Write the report", "startAt": "…",
                            "endAt": "…", "plannedMinutes": 120, "actualMinutes": 45, "stillAssigned": true } ],
              "blockMinutes": 120, "workingMinutes": 420 } ] }
```

### Focus (`/me/focus`)

The tasks to focus on: open (not complete), not deleted, not meetings, and assigned to the caller. They come in four groups, always in this order:

| Group | Due date |
|---|---|
| `overdue` | before today |
| `due_today` | today |
| `due_this_week` | after today, up to the Sunday of today's Monday-based week (`weekEnd`) |
| `backlog` | no due date, or a later due date |

**Order within a group:** fully planned last; then priority (critical, high, medium, low); then due date (none last); then title.

**Size:** each group lists its first 50 items; `count` is the full number.

**Item fields:**
- `remainingMinutes` = estimate − `loggedMinutes`, never below 0, or `null` without an estimate.
- `loggedMinutes` counts **everyone's** clocking sessions on the task, with an open one counted up to now.
- `plannedAheadMinutes`, `firstPlannedDate` and `plannedDayCount` describe the caller's blocks from today on.
- `fullyPlanned` is true when those blocks cover `remainingMinutes`; it is never true without an estimate.

```json
{ "today": "2026-11-02", "weekEnd": "2026-11-08",
  "groups": [ { "reason": "overdue", "count": 1,
                "items": [ { "taskId": "…", "projectId": "…", "projectName": "Portal", "title": "Payroll export fix",
                             "priority": "high", "dueDate": "2026-10-31", "estimatedMinutes": 300, "loggedMinutes": 90,
                             "remainingMinutes": 210, "plannedAheadMinutes": 120, "firstPlannedDate": "2026-11-04",
                             "plannedDayCount": 1, "fullyPlanned": false, "reason": "overdue" } ] },
              { "reason": "due_today", "count": 0, "items": [] },
              { "reason": "due_this_week", "count": 0, "items": [] },
              { "reason": "backlog", "count": 0, "items": [] } ] }
```

## Rules

- **Block:**
  - The task is assigned to the caller and is not a meeting.
  - At least 15 minutes, starting and ending on the same local day.
  - The day is today or up to 28 days ahead.
  - It never overlaps another of the caller's blocks.
  - A block never changes the task's due date, estimate or status.
- **Past days:** a block already on a past day can't be moved or deleted.
- **`editable`:** true for the caller's own days from today to today + 28.
- **Actual:**
  - For a task: its clocked minutes (task clock-in sessions) inside the day, with an open session counting up to now.
  - For a meeting: its duration once it has ended, unless declined or cancelled (all-day: 8 h per day).
  - Meetings are the Phase 2 task meetings the employee organizes or attends.
- **Private meetings:** a manager sees one as "Private meeting" (spec D17).
- **`attendanceMinutes`:** the attendance clock for that day, shown beside planned vs actual. It is the recorded worked minutes once clocked out, or the time since clock-in minus breaks while still clocked in, and 0 with no attendance record.

## Errors (`ProblemDetails.code`)

| Status | Code |
|---|---|
| 503 | `planner_disabled` |
| 400 | `planner_day_read_only` (a past day or a block on one) |
| 400 | `planner_beyond_horizon` (more than 28 days ahead) |
| 400 | `planner_invalid_range` (`to` before `from`, or more than 42 days) |
| 400 | `planner_invalid_time` (under 15 minutes, or crossing midnight) |
| 400 | `planner_task_not_plannable` |
| 400 | `planner_not_today` (v1; no longer returned, kept in client error maps during rollout) |
| 409 | `planner_block_overlap` |
| 404 | `planner_not_found` (not visible to the caller, same body for "does not exist") |
