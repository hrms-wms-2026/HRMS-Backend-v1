# Get Sprint Tasks

**GET** `/api/v1/work/sprints/{sprintId}/tasks`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + (`projects:read`/`*` OR an active membership on the sprint's Objective or any of its ancestors — checked in-handler against the parent Objective, same pattern as Get Objective Subtree).
**Idempotent:** Yes.

## Description

Returns the tasks that belong to `{sprintId}` only (server-side filter via `GetBySprintIdAsync`, not the objective-wide list). This is the Tree tab's "expand a Sprint node" fetch. Assignees are populated the same way as Get Objective Tasks.

## Request

No body. `{sprintId}` is a path parameter (`id` in the controller route `sprints/{id:guid}/tasks`).

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
    "categoryId": "guid",
    "statusId": "guid",
    "priority": "medium",
    "storyPoints": 5,
    "dueDate": "2026-09-01",
    "estimatedHours": 8,
    "completedHours": 0,
    "progressPercent": 0,
    "sprintId": "guid",
    "assigneeEmployeeIds": ["guid"]
  }
]
```

`sprintId` on every item is this sprint's id. `assigneeEmployeeIds` is an empty array when the task has no assignees.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or has neither `projects:read`/`*` nor an active membership on the sprint's Objective or an ancestor of it |
| `404` | Sprint doesn't exist in tenant, or its Objective is missing |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`GetTasks`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetSprintTasks/GetSprintTasksQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-2-get-sprint-tasks-endpoint.md`
