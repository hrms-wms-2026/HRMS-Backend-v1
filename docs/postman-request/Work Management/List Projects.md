# List Projects

**GET** `/api/v1/work/projects/mine` — caller's own projects. **Permission:** `projects:access` (the module-wide base gate — every Work Management endpoint requires this).
**GET** `/api/v1/work/projects?employeeId={employeeId}` — any given employee's projects. **Permission:** `projects:read` (the separate "view others" grant, admin/company-owner path).

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.

## Description

Both routes return the target employee's active `project_members` rows joined to active `projects`, deduplicated on `project_id` (an employee can have more than one active membership on the same project via different Objectives). Query params: `pageNumber` (default 1), `pageSize` (default 20, capped 100), `sortBy` (`name` | `startDate` | `targetDate`, default sorts by creation date), `sortDirection` (`asc` | `desc`, default `asc`).

If `employeeId` doesn't resolve to any employee with active memberships in the tenant, the response is an empty page (`200 OK`, `items: []`) — list semantics, not `404`.

**Breaking change (2026-08-14):** the "view others" query parameter was renamed from `userId` to `employeeId`. Its value is now an `employees.id`, not a `users.id`. `leadId` in the response is also now an Employee id; the JSON field name is unchanged. Clients that were caching or comparing against the old UserId-space value must re-fetch.

## Response

`200 OK`

```json
{
  "items": [
    { "id": "guid", "name": "string", "identifier": "string", "categoryId": "guid", "leadId": "guid",
      "startDate": "date", "targetDate": "date", "color": "string|null", "isActive": true,
      "allocatedHours": "decimal", "completedHours": "decimal", "isLead": true,
      "isAchieved": false, "achievedAt": "datetime|null" }
  ],
  "pageNumber": 1, "pageSize": 20, "totalCount": 3, "totalPages": 1, "hasNext": false, "hasPrevious": false
}
```

`isAchieved`/`achievedAt` added 2026-08-09 (previously missing from this response despite `GET /work/projects/{id}` already returning them — see `docs/superpowers/plans/finished/2026-08-09/2026-08-08-work-management-frontend-blocking-endpoints.md`).

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access` (either route), or (`?employeeId=` route only) lacks `projects:read` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`ListMine`, `ListByUser`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/ListProjects/ListProjectsQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md`, `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 25 identity change)
