# Delete Task Status

**DELETE** `/api/v1/work/task-statuses/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** No — a second call on an already-deleted id returns `404`.

## Description

Removes a status column from the Project's task status template. Only template rows (`objectiveId` null)
can be deleted through this route. Blocked while any active task is still sitting in this status.

## Request

No body. `id` is a path parameter (the status id).

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not an owner/member of the Project (checked via its default Objective's effective-manager status) |
| `404` | Status not found, the status is an orphaned per-Objective row rather than the Project template, the Project is not found/inactive, or the Project has no default milestone |
| `409` | At least one active task is still assigned to this status — move them out first |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskStatus/DeleteTaskStatusCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-1-collapse-task-status-to-project-scope.md`
