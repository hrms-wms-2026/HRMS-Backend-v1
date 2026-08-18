# Get Objective Tasks

**GET** `/api/v1/work/objectives/{objectiveId}/tasks`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond tenant session (handler authenticates).
**Idempotent:** Yes.

## Description

Returns the flat list of tasks for an Objective. Board grouping by `statusId` is a client concern; the same payload serves Board and Backlog.

## Request

No body. `objectiveId` is a path parameter.

## Response

`200 OK`

```json
[
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
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTasks/GetObjectiveTasksQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-1-schema-and-crud.md`
