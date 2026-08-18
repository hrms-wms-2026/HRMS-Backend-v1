# Edit Task Status

**PATCH** `/api/v1/work/objectives/{objectiveId}/task-statuses/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** Yes for identical payloads.

## Description

Renames, reorders, or toggles `requiresApproval` on an Objective-scoped status column. Objective-owner only. Project-template rows (`objectiveId` null) cannot be edited through this route.

## Request

```json
{
  "name": "In Review",
  "displayOrder": 2,
  "requiresApproval": true,
  "approverId": "guid-or-null"
}
```

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not the Objective owner |
| `404` | Status or Objective not found, or the status is a Project template rather than an Objective copy |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-1-schema-and-crud.md`
