# Get Project

**GET** `/api/v1/work/projects/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:read` **OR** an active `project_members` row for this project — checked in this order by the handler, not by `[RequirePermission]` (which would hard-block members lacking the tenant-wide permission).

## Description

Gets a single Project. A soft-deleted project (`is_active = false`) is treated as not found. `isLead` is always computed directly as `project.leadId == callerId`, independent of which access path (permission vs. membership) was used.

## Response

`200 OK`

```json
{
  "id": "guid", "name": "string", "identifier": "string", "categoryId": "guid", "description": "string|null",
  "leadId": "guid", "startDate": "date", "targetDate": "date", "color": "string|null",
  "actualHours": "decimal|null", "allocatedHours": "decimal", "completedHours": "decimal",
  "isActive": true, "createdAt": "datetime", "updatedAt": "datetime|null", "isLead": true
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller has neither `projects:read` nor an active membership row for this project |
| `404` | Project doesn't exist in tenant, or exists but `is_active = false` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`GetById`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectById/GetProjectByIdQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md`
