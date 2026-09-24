# Reorder Task Categories

**POST** `/api/v1/work/projects/{projectId}/task-categories/reorder`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** Yes for an identical payload.

## Description

Bulk-updates `displayOrder` across the Project's entire task category list in one call. Every category id
referenced must belong to this Project.

## Request

```json
{
  "updates": [
    { "categoryId": "guid", "displayOrder": 0 },
    { "categoryId": "guid", "displayOrder": 1 }
  ]
}
```

## Response

`200 OK` — the full, reordered category list for the Project:

```json
[
  { "id": "guid", "name": "Feature", "displayOrder": 0 },
  { "id": "guid", "name": "Bug", "displayOrder": 1 }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not an owner/member of the Project (checked via its default Objective's effective-manager status) |
| `404` | Project not found/inactive, the Project has no default milestone, or an `updates[].categoryId` does not belong to this Project |
| `422` | `updates` is empty/contains nulls/duplicate category ids, or a negative `displayOrder` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskCategories/ReorderTaskCategoriesCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-4-task-category-crud-and-docs.md`
