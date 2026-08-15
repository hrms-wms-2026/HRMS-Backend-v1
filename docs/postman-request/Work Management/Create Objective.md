# Create Objective

**POST** `/api/v1/work/objectives`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be the parent Objective's current Head.

## Description

Creates a sub-milestone under an existing Objective. `headUserId` (optional) assigns a different Head than the creator; omit it to default to the creator (design §5). Rejected with `400` if the new milestone's date range or allocated hours would fall outside the parent's.

Also syncs project membership for the resolved Head (creates or reactivates a `project_members` row scoped to the new milestone) and auto-grants `projects:access` if they don't already have it (takes effect on their next login - see design doc §7).

## Request

```json
{ "parentObjectiveId": "guid", "title": "Design Phase", "description": "optional", "startDate": "2026-01-15", "endDate": "2026-03-01", "allocatedHours": 20, "headUserId": "guid|null" }
```

## Response

`201 Created`

```json
{ "id": "guid", "projectId": "guid", "parentObjectiveId": "guid", "isDefault": false, "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid", "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": null, "allocatedHours": 20, "completedHours": 0, "isActive": true, "createdAt": "datetime", "updatedAt": null }
```

**Breaking change (2026-08-14):** `ownerId` and `reportingManagerId` now carry `employees.id` values, not `users.id`. Field names are unchanged. `headUserId` is still accepted on the request (JSON name unchanged) but the current handler ignores it and always assigns the creator as owner — optional-head / invitation assignment is backend Tasks 1–12, not yet implemented. Clients that were caching or comparing against the old UserId-space value must re-fetch.

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure, date range/hours would exceed the parent's, or the assigned head must be an active employee in this tenant |
| `403` | Caller is not the parent Objective's current Head |
| `404` | Parent Objective doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Create`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
