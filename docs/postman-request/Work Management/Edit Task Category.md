# Edit Task Category

**PATCH** `/api/v1/work/task-categories/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** Yes for identical payloads.

## Description

Renames or reorders a Project's task category row.

## Request

```json
{
  "name": "Bugfix",
  "displayOrder": 1
}
```

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not an owner/member of the Project (checked via its default Objective's effective-manager status: the Objective's owner, an active member, or the owner/an active member of any ancestor) |
| `404` | Category not found, the Project is not found/inactive, or the Project has no default milestone |
| `422` | Validation failure (missing name, name too long, negative display order) |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskCategory/EditTaskCategoryCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-4-task-category-crud-and-docs.md`
