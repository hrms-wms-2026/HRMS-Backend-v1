# Move Task Status

**PATCH** `/api/v1/work/tasks/{id}/status`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** Yes for the same `newStatusId`.

## Description

Moves a task to another status column. Unconditional in this slice (`task_approvals` is deferred). If the target status has `marksTaskComplete`, sets `completedAt` and `progressPercent = 100`.

## Request

```json
{
  "newStatusId": "guid"
}
```

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated |
| `404` | Task or target status not found |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-1-schema-and-crud.md`
