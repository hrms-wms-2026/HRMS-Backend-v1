# Get Objective Task Statuses

**GET** `/api/v1/work/objectives/{objectiveId}/task-statuses`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond tenant session (handler authenticates).
**Idempotent:** Yes for reads; first access copies the Project template into Objective-scoped rows.

## Description

Returns the Objective's status columns, ordered by `displayOrder`. If none exist yet, copies the Project-level template (`objective_id` null) into this Objective.

## Request

No body. `objectiveId` is a path parameter.

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
| `404` | Objective not found |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTaskStatuses/GetObjectiveTaskStatusesQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-1-schema-and-crud.md`
