# Delete Task Category

**DELETE** `/api/v1/work/task-categories/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** No — a second call on an already-deleted id returns `404`.

## Description

Removes a category from the Project's task category list. Blocked while any active (including
soft-deleted) task still references this category.

## Request

No body. `id` is a path parameter (the category id).

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not an owner/member of the Project (checked via its default Objective's effective-manager status) |
| `404` | Category not found, the Project is not found/inactive, or the Project has no default milestone |
| `409` | At least one task still references this category — move them out first |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskCategory/DeleteTaskCategoryCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-21-work-management-project-scoped-task-status-and-category/part-4-task-category-crud-and-docs.md`
