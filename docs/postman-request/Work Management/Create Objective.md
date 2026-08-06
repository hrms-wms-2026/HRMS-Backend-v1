# Create Objective

**POST** `/api/v1/work/objectives`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be the parent Objective's current Head.

## Description

Creates a sub-milestone under an existing Objective. `headUserId` (optional) assigns a different Head than the creator; omit it to default to the creator (design §5). Rejected with `400` if the new milestone's date range or allocated hours would fall outside the parent's.

## Request

```json
{ "parentObjectiveId": "guid", "title": "Design Phase", "description": "optional", "startDate": "2026-01-15", "endDate": "2026-03-01", "allocatedHours": 20, "headUserId": "guid|null" }
```

## Response

`201 Created`

```json
{ "id": "guid", "projectId": "guid", "parentObjectiveId": "guid", "isDefault": false, "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid", "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": null, "allocatedHours": 20, "completedHours": 0, "isActive": true, "createdAt": "datetime", "updatedAt": null }
```

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure, or date range/hours would exceed the parent's |
| `403` | Caller is not the parent Objective's current Head |
| `404` | Parent Objective doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Create`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
