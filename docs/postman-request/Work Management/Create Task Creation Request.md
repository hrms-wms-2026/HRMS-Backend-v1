# Create Task Creation Request

**POST** `/api/v1/work/objectives/{objectiveId}/task-creation-requests`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond `TenantPolicy` — in-handler checks require an active Objective member who is **not** the owner.
**Idempotent:** No — each successful call creates a new pending request.

## Description

Non-owner Objective member submits a request to create a task. Slack is **not** checked at submission time; the owner re-checks slack on approve. The milestone owner creates tasks directly via `POST .../tasks` instead.

## Request

```json
{
  "title": "Build the login page",
  "description": "optional",
  "taskType": "task",
  "priority": "medium",
  "dueDate": "2026-09-01",
  "estimatedHours": 8,
  "storyPoints": 5,
  "sprintId": null
}
```

`taskType` is one of `task`, `bug`, `story`, `feature`. `priority` is one of `low`, `medium`, `high`, `critical`. `estimatedHours`, `dueDate`, `description`, `storyPoints`, and `sprintId` are optional. Omit `sprintId` (or send `null`) to request a direct task under the Objective with no Sprint.

## Response

`202 Accepted`

```json
{
  "id": "guid",
  "objectiveId": "guid",
  "status": "pending",
  "payload": {
    "title": "Build the login page",
    "description": "optional",
    "taskType": "task",
    "priority": "medium",
    "dueDate": "2026-09-01",
    "estimatedHours": 8,
    "storyPoints": 5,
    "sprintId": null
  },
  "createdAt": "2026-08-17T00:00:00+00:00"
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure, or caller is the milestone owner (owners create tasks directly) |
| `403` | Not authenticated, no employee record, or caller is not an active milestone member |
| `404` | Objective not found / inactive, or (when provided) Sprint not found |
| `409` | The provided Sprint is Achieved (frozen) |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`CreateRequest`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCreationRequest/CreateTaskCreationRequestCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-2-task-creation-requests.md`
