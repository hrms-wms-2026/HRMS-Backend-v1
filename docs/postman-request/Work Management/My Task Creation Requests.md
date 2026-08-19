# My Task Creation Requests

**GET** `/api/v1/work/task-creation-requests/mine`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond `TenantPolicy` — returns pending requests for Objectives the caller currently owns.

## Description

The caller's approval queue: every `pending` task-creation request whose Objective's **current** owner is the caller (owner is looked up live, not snapshotted at request time).

## Response

`200 OK` — a JSON array of `TaskCreationRequest` objects (same shape as Create Task Creation Request's 202 body).

```json
[
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
      "storyPoints": 5
    },
    "createdAt": "2026-08-17T00:00:00+00:00"
  }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or no employee record for the current user |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`MyRequests`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyTaskCreationRequests/GetMyTaskCreationRequestsQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-2-task-creation-requests.md`
