# Edit Task

**PATCH** `/api/v1/work/tasks/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** Yes for identical payloads.

## Description

Edits task fields. Increasing `estimatedHours` re-runs the slack check (the task's current hours are excluded from the sum).

If the task belongs to an active calendar Event (directly, or via a whole-module link on its Objective), a `dueDate` change that would move it outside that Event's `[startDate, endDate]` window is rejected with `409` — widen the Event first. Clearing `dueDate` on such a task is also `409`. The same guard applies to the approved-edit-request path.

## Request

```json
{
  "title": "Build the login page",
  "description": "optional",
  "priority": "high",
  "dueDate": "2026-09-15",
  "estimatedHours": 12,
  "storyPoints": 8
}
```

## Response

`200 OK` — same `WorkTaskViewModel` shape as Create Task.

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure |
| `403` | Not authenticated |
| `404` | Task or Objective not found |
| `409` | New `estimatedHours` exceeds remaining slack, or a `dueDate` change moves an event-member task outside its Event window (or clears the due date) |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-1-schema-and-crud.md`
