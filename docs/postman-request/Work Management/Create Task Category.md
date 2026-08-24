# Create Task Category

**POST** `/api/v1/work/projects/{projectId}/task-categories`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** No — creates a new row each call.

## Description

Adds a new category to the Project's task category list. `projectId` is a path parameter.

## Request

```json
{
  "name": "Chore",
  "displayOrder": 2
}
```
`name`: required, max 100 characters. `displayOrder`: integer, must not be negative.

## Response

`201 Created`

```json
{ "id": "guid", "name": "Chore", "displayOrder": 2 }
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not an owner/member of the Project (checked via its default Objective's effective-manager status) |
| `404` | Project not found/inactive, or the Project has no default milestone |
| `422` | Validation failure (missing name, name too long, negative display order) |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCategory/CreateTaskCategoryCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-4-task-category-crud-and-docs.md`
