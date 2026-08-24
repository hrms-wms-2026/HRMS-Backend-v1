# Get Objective Tasks

**GET** `/api/v1/work/objectives/{objectiveId}/tasks`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + (`projects:read`/`*` OR an active membership on this milestone or any of its ancestors — checked in-handler, not via `[RequirePermission]`, same pattern as Get Objective Subtree).
**Idempotent:** Yes.

## Description

Returns the flat list of tasks for an Objective (direct tasks and sprint tasks together; distinguished by nullable `sprintId`). Board grouping by `statusId` is a client concern; the same payload serves Board and Backlog.

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

`sprintId` is `null` for direct (non-sprint) tasks. `assigneeEmployeeIds` is an empty array when the task has no assignees.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or has neither `projects:read`/`*` nor an active membership on this milestone or an ancestor of it |
| `404` | Objective doesn't exist in tenant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`GetByObjective`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTasks/GetObjectiveTasksQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-1-fix-sprint-and-task-authorization.md`
