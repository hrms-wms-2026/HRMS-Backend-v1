# Get Project Task Statuses

**GET** `/api/v1/work/projects/{projectId}/task-statuses`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond tenant session (handler authenticates).
**Idempotent:** Yes.

## Description

Returns the Project's task status template (`objectiveId` null), ordered by `displayOrder`. Every task in
every Objective under this Project shares this same status list — there is no longer a per-Objective copy.

## Request

No body. `projectId` is a path parameter.

## Response

`200 OK`

```json
[
  { "id": "guid", "name": "To Do", "displayOrder": 0, "requiresApproval": false, "approverId": null, "marksTaskComplete": false },
  { "id": "guid", "name": "In Process", "displayOrder": 1, "requiresApproval": false, "approverId": null, "marksTaskComplete": false },
  { "id": "guid", "name": "Review", "displayOrder": 2, "requiresApproval": false, "approverId": null, "marksTaskComplete": false },
  { "id": "guid", "name": "Done", "displayOrder": 3, "requiresApproval": false, "approverId": null, "marksTaskComplete": true }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated |
| `404` | Project not found or inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTaskStatuses/GetProjectTaskStatusesQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-1-collapse-task-status-to-project-scope.md`
