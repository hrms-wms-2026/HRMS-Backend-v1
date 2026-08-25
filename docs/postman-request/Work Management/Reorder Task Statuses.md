# Reorder Task Statuses

**POST** `/api/v1/work/projects/{projectId}/task-statuses/reorder`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** Yes for an identical payload.

## Description

Bulk-updates `displayOrder`, `visibility`, and `marksTaskComplete` across the Project's entire task status
template in one call. Every status id referenced must belong to this Project's template. Exactly one
status in the full resulting set must have `marksTaskComplete: true` — enforced both before and after
applying the updates.

## Request

```json
{
  "updates": [
    { "statusId": "guid", "displayOrder": 0, "visibility": "public", "marksTaskComplete": false },
    { "statusId": "guid", "displayOrder": 1, "visibility": "public", "marksTaskComplete": true }
  ]
}
```

## Response

`200 OK` — the full, reordered status list for the Project:

```json
[
  { "id": "guid", "name": "To Do", "displayOrder": 0, "requiresApproval": false, "approverId": null, "marksTaskComplete": false, "visibility": "public" },
  { "id": "guid", "name": "Done", "displayOrder": 1, "requiresApproval": false, "approverId": null, "marksTaskComplete": true, "visibility": "public" }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not an owner/member of the Project (checked via its default Objective's effective-manager status) |
| `404` | Project not found/inactive, the Project has no default milestone, or an `updates[].statusId` does not belong to this Project's template |
| `422` | `updates` is empty/contains nulls/duplicate status ids, an invalid `visibility` value, a negative `displayOrder`, or the resulting set does not have exactly one `marksTaskComplete: true` row |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-1-collapse-task-status-to-project-scope.md`
