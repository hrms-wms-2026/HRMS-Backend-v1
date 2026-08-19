# Get Work Notification Navigation

**GET** `/api/v1/work/notification-navigation?relatedEntityType={type}&relatedEntityId={guid}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.

## Description

Resolves a Work Management in-app notification's `relatedEntityType` / `relatedEntityId` into route pieces for the frontend bell click-through (Board vs Approvals).

Supported `relatedEntityType` values:
- `task` → `targetTab: board` (+ `taskId`)
- `task_creation_request` → `targetTab: board` (+ `taskId` when the request was approved and created a task)
- `objective_change_request` / `allocation_extend` → `targetTab: approvals`

## Response

`200 OK`

```json
{
  "projectId": "guid",
  "objectiveId": "guid",
  "taskId": "guid-or-null",
  "targetTab": "board"
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | Unsupported `relatedEntityType` |
| `403` | Not authenticated |
| `404` | Related entity or its objective not found |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetWorkNotificationNavigation/`
