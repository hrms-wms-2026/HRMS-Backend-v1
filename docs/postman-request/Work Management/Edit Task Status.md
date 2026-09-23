# Edit Task Status

**PATCH** `/api/v1/work/task-statuses/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** Yes for identical payloads.

## Description

Renames, reorders, or toggles `requiresApproval`/`visibility` on a Project's task status template row.
Only template rows (`objectiveId` null) can be edited — a status row left over from before this Part's
per-Objective-copy model (`objectiveId` set) is rejected as not found, not silently edited.

## Request

```json
{
  "name": "In Review",
  "displayOrder": 2,
  "requiresApproval": true,
  "approverId": "guid-or-null",
  "visibility": "public"
}
```
`visibility` must be `"public"` or `"private"`.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not an owner/member of the Project (checked via its default Objective's effective-manager status: the Objective's owner, an active member, or the owner/an active member of any ancestor) |
| `404` | Status not found, the status is an orphaned per-Objective row rather than the Project template, the Project is not found/inactive, or the Project has no default milestone |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-1-collapse-task-status-to-project-scope.md`
