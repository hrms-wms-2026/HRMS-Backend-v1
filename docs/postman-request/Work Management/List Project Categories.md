# List Project Categories

**GET** `/api/v1/work/project-categories`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` (the module-wide base gate, same as every other Work Management endpoint).

## Description

Lists the tenant's Project Categories — a flat, tenant-wide reference list (not scoped to any project or user), used to populate the category picker on Create/Edit Project and the Project list's category filter. Active-only by default; pass `includeInactive=true` to include inactive categories too. Ordered by `name`.

Query params: `includeInactive` (boolean, default `false`).

## Response

`200 OK`

```json
[
  { "id": "guid", "name": "string" }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectCategoriesController.cs` (`List`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/ListProjectCategories/ListProjectCategoriesQueryHandler.cs`
Repository: `IProjectCategoryRepository.GetAllForTenantAsync` (`src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectCategoryRepository.cs`)
Request/report: `docs/superpowers/plans/finished/2026-08-09/2026-08-08-work-management-frontend-blocking-endpoints.md`
