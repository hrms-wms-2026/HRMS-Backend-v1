# Day Planner API v1

Design: `docs/superpowers/specs/2026-10-26-meeting-hours-and-day-planner-design.md` (Part B). Web only. An employee plans
**today only**, in their own timezone (the zone `GET /api/v1/calendar/my-timezone` returns). Feature flag `Planner:Enabled`
(default off): every endpoint then answers **503 `planner_disabled`**.

## Endpoints

| Method | Route | Who | Body |
|---|---|---|---|
| GET | `/api/v1/planner/me?date=YYYY-MM-DD` | the caller (date defaults to today; a future date is 400) | — |
| GET | `/api/v1/planner/employees/{employeeId}?date=` | the employee, or any manager in their reporting-line ancestor chain; anyone else 404 | — |
| POST | `/api/v1/planner/me/blocks` | the caller | `{ taskId, startAt, endAt }` |
| PATCH | `/api/v1/planner/me/blocks/{blockId}` | the block's owner | `{ startAt, endAt }` |
| DELETE | `/api/v1/planner/me/blocks/{blockId}` | the block's owner | — |

Every call returns the day (`PlannerDayResponse`); writes return the caller's today after the change.

```json
{ "date": "2026-11-02", "timezone": "Asia/Colombo", "editable": true,
  "blocks":   [ { "id": "…", "taskId": "…", "taskTitle": "Write the report", "startAt": "…", "endAt": "…",
                  "plannedMinutes": 120, "actualMinutes": 45, "stillAssigned": true, "overlapsMeeting": false } ],
  "meetings": [ { "taskId": "…", "seriesId": "…", "originalStart": "…", "startAt": "…", "endAt": "…", "title": "Weekly sync",
                  "isPrivate": false, "cancelled": false, "declined": false, "plannedMinutes": 60, "actualMinutes": 60 } ],
  "dueTasks": [ { "taskId": "…", "title": "…", "dueDate": "2026-11-02", "estimatedHours": 5 } ],
  "totals":   { "plannedMinutes": 180, "actualMinutes": 105 } }
```

## Rules

- A block: the task is assigned to the caller and is not a meeting; at least 15 minutes; starts and ends on the caller's
  today; never overlaps another of their blocks. A block never changes the task's due date, estimate or status.
- `editable` is true only for the caller's own today; earlier days are read-only.
- Actual: a task's clocked minutes (task clock-in sessions) inside the day, an open session counting up to now; a meeting's
  duration once it has ended unless declined or cancelled (all-day: 8 h per day). Meetings are the Phase 2 task meetings
  the employee organizes or attends.
- A manager sees a private meeting as "Private meeting" (spec D17).

## Errors (`ProblemDetails.code`)

| Status | Code |
|---|---|
| 503 | `planner_disabled` |
| 400 | `planner_not_today`, `planner_invalid_time`, `planner_task_not_plannable` |
| 409 | `planner_block_overlap` |
| 404 | `planner_not_found` (not visible to the caller, same body for "does not exist") |
