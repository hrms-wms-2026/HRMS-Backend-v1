# Get Project Task Categories

**GET** `/api/v1/work/projects/{projectId}/task-categories`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond tenant session (handler authenticates).
**Idempotent:** Yes.

## Description

Returns the Project's task category list, ordered by `displayOrder`. Every task in every Objective under
this Project shares this same category list — categories are seeded once (from a default template) at
Project creation and are never per-Objective.

## Request

No body. `projectId` is a path parameter.

## Response

`200 OK`

```json
[
  { "id": "guid", "name": "Feature", "displayOrder": 0 },
  { "id": "guid", "name": "Bug", "displayOrder": 1 },
  { "id": "guid", "name": "Chore", "displayOrder": 2 }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated |
| `404` | Project not found or inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTaskCategories/GetProjectTaskCategoriesQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-4-task-category-crud-and-docs.md`
