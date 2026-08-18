# Create Task

**POST** `/api/v1/work/objectives/{objectiveId}/tasks`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** No — each successful call creates a new task.

## Description

Objective-owner direct create. Blocked with `409` when `estimatedHours` exceeds remaining slack on the Objective (`allocated_hours - child allocated hours - existing task estimated hours`). Non-owners must use the task-creation-request flow instead.

## Request

```json
{
  "title": "Build the login page",
  "description": "optional",
  "taskType": "task",
  "priority": "medium",
  "dueDate": "2026-09-01",
  "estimatedHours": 8,
  "storyPoints": 5
}
```

`taskType` is one of `task`, `bug`, `story`, `feature`. `priority` is one of `low`, `medium`, `high`, `critical`. `estimatedHours`, `dueDate`, `description`, and `storyPoints` are optional.

## Response

`201 Created`

```json
{
  "id": "guid",
  "objectiveId": "guid",
  "shortId": "WEB-7",
  "title": "Build the login page",
  "description": "optional",
  "taskType": "task",
  "statusId": "guid",
  "priority": "medium",
  "storyPoints": 5,
  "dueDate": "2026-09-01",
  "estimatedHours": 8,
  "completedHours": 0,
  "progressPercent": 0
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure (title, type, priority, negative hours) |
| `403` | Not authenticated, no employee record, or caller is not the Objective owner |
| `404` | Objective or Project not found / inactive |
| `409` | `estimatedHours` exceeds remaining slack; body is `InsufficientAllocationResponse` (`availableSlackHours`, `suggestedAction: "extend_allocation"`) |
| `422` | No task statuses configured for this milestone yet |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-1-schema-and-crud.md`
