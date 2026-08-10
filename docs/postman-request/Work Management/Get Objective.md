# Get Objective

**GET** `/api/v1/work/objectives/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:read`/`*` OR an active membership on this milestone or any of its ancestors — checked in-handler, not via `[RequirePermission]` (same pattern as Get Project and Get Objective Tree).

## Description

Gets a single milestone by id.

## Response

`200 OK`

```json
{
  "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false,
  "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid|null",
  "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": "decimal|null",
  "allocatedHours": "decimal", "completedHours": "decimal", "isActive": true, "isAchieved": false,
  "achievedAt": "datetime|null", "createdAt": "datetime", "updatedAt": "datetime|null"
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller has neither `projects:read`/`*` nor an active membership on this milestone or an ancestor of it |
| `404` | Milestone doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetById`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
