# Get Objective Tree

**GET** `/api/v1/work/projects/{projectId}/objectives`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** none — caller must have an active `project_members` row somewhere in this project (membership fallback, no `[RequirePermission]` attribute).

## Description

Every active Objective for a Project, flat (client builds the tree from `parentObjectiveId`). No admin/cross-user visibility permission exists for this endpoint — membership is the only access path (design §6 #8).

## Response

`200 OK` — a JSON array of reachable Objectives (flat; client builds the tree from `parentObjectiveId`):

```json
[
  {
    "id": "guid",
    "parentObjectiveId": "guid|null",
    "isDefault": true,
    "title": "string",
    "ownerId": "guid",
    "startDate": "date",
    "endDate": "date",
    "allocatedHours": 40,
    "completedHours": 0,
    "isActive": true,
    "isAchieved": false,
    "progress": 12.5,
    "ownerName": "Ada Lovelace",
    "isOwner": true
  }
]
```

`progress` is the Objective's stored progress percent. `ownerName` is the resolved display name for `ownerId` (null if the employee cannot be resolved). `isOwner` is true only when the caller has **direct active membership on this exact Objective** — ancestor/descendant visibility does not set it. Use `isOwner` to gate per-row action icons: a node that is only in the tree as ancestor context has `isOwner: false`.

**Breaking change (2026-08-14):** `ownerId` now carries an `employees.id` value, not a `users.id`. The field name is unchanged. Clients that were caching or comparing against the old UserId-space value must re-fetch.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller has no active membership in this project |
| `404` | Project doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetTree`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-3-enrich-objective-tree-response.md`
