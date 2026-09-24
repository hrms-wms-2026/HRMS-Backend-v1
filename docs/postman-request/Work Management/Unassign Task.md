# Unassign Task

**DELETE** `/api/v1/work/tasks/{id}/assignments/{employeeId}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** Yes — a missing assignment returns `404` rather than deleting twice.

## Description

Removes the assignment for the given employee on the task.

## Request

No body. `id` and `employeeId` are path parameters.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated |
| `404` | Task or assignment not found |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/UnassignTask/UnassignTaskCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-1-schema-and-crud.md`
