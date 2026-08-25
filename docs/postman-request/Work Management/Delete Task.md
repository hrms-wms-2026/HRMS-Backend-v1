# Delete Task

**DELETE** `/api/v1/work/tasks/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this task's owning Objective's current owner. There is no tenant-permission-bypass path (same as Delete Task Status / Unassign Task).
**Idempotent:** No — a second call on an already-deleted task returns `404` (the global EF query filter hides the soft-deleted row, so the handler sees it as missing).

## Description

Soft-deletes a Task. `WorkTask` already has `IsDeleted`/`DeletedAt` via `BaseEntity`; the handler calls `IWorkTaskRepository.Remove`, and `SoftDeleteInterceptor` converts that EF `Remove()` into `IsDeleted = true` + `DeletedAt = now` on `SaveChanges`. Subsequent list/get queries omit the row via the global query filter. Assignments and related request rows are left in place (same as Project/Objective soft-delete).

## Request

No body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or is not an effective manager of this milestone (its owner, an active member, or the owner/an active member of any ancestor milestone) |
| `404` | Task doesn't exist in tenant, is already soft-deleted, or its Objective is missing |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`Delete`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTask/DeleteTaskCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-4-delete-task-soft-delete.md`
