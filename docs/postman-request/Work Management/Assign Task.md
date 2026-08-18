# Assign Task

**POST** `/api/v1/work/tasks/{id}/assignments`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** No — assigning an already-assigned employee returns `409`.

## Description

Adds an assignment for an active employee. Stores both `employeeId` and the employee's `userId`. No HR-availability enrichment in this slice.

## Request

```json
{
  "employeeId": "guid"
}
```

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | Assignee is not an active employee in this tenant |
| `403` | Not authenticated / no employee record |
| `404` | Task not found |
| `409` | Employee is already assigned to this task |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AssignTask/AssignTaskCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-1-schema-and-crud.md`
