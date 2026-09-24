# Get Project Tasks

**GET** `/api/v1/work/projects/{projectId}/tasks`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:read` or active project-member objective access (handler enforces this).
**Idempotent:** Yes.

## Description

Returns every task belonging to an Objective under the requested Project. The response includes tasks without a
Sprint (`sprintId: null`); sprint filtering is intentionally left to the frontend. Callers with `projects:read`
or `*` see all project tasks, while other callers see only tasks for Objectives returned by their active project
membership.

Optional query parameter `assigneeEmployeeIds` (repeatable) filters the result to tasks assigned to one of the
given employees; omitting it or passing none returns all tasks.

## Request

No body. `projectId` is a path parameter.

```
GET /api/v1/work/projects/{projectId}/tasks?assigneeEmployeeIds=emp-guid-1&assigneeEmployeeIds=emp-guid-2
```

## Response

`200 OK`

```json
[
  {
    "id": "guid",
    "objectiveId": "guid",
    "shortId": "PRJ-1",
    "title": "Implement project board",
    "description": null,
    "categoryId": "guid",
    "statusId": "guid",
    "priority": "medium",
    "storyPoints": 3,
    "dueDate": "2026-08-31",
    "estimatedHours": 8,
    "completedHours": 0,
    "progressPercent": 0,
    "sprintId": null,
    "assigneeEmployeeIds": [],
    "activeEventId": null,
    "activeEventName": null
  }
]
```

`activeEventId` / `activeEventName` are populated when the task is directly linked to an active calendar Event.

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, missing tenant/employee context, or no accessible Objective membership |
| `404` | Project not found or inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTasks/GetProjectTasksQueryHandler.cs`
Plan: `docs/superpowers/specs/next/2026-08-24-work-management-board-backlog-project-wide-design.md`
