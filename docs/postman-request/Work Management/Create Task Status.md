# Create Task Status

**POST** `/api/v1/work/projects/{projectId}/task-statuses`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** No — creates a new row each call.

## Description

Adds a new status column to the Project's task status template. `projectId` is a path parameter.

## Request

```json
{
  "name": "Blocked",
  "displayOrder": 4,
  "visibility": "public",
  "marksTaskComplete": false,
  "requiresApproval": false,
  "approverId": null
}
```
`name`: required, max 100 characters. `displayOrder`: integer, must not be negative. `visibility`: must be
`"public"` or `"private"`.

## Response

`201 Created`

```json
{ "id": "guid", "name": "Blocked", "displayOrder": 4, "requiresApproval": false, "approverId": null, "marksTaskComplete": false, "visibility": "public" }
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not an owner/member of the Project (checked via its default Objective's effective-manager status) |
| `404` | Project not found/inactive, or the Project has no default milestone |
| `422` | Validation failure (missing name, name too long, negative display order, invalid visibility) |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-1-collapse-task-status-to-project-scope.md`
