# Get Objective Sprints

**GET** `/api/v1/work/objectives/{objectiveId}/sprints`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + (`projects:read`/`*` OR an active membership on this milestone or any of its ancestors — checked in-handler, not via `[RequirePermission]`, same pattern as Get Objective Subtree).
**Idempotent:** Yes.

## Description

Returns the sprints owned by `{objectiveId}`. Pass `activeOnly=true` to restrict the list to Active sprints (the Backlog member view); omit or `false` for the full list including Future / Complete / Incomplete / Achieved.

## Request

No body. `objectiveId` is a path parameter. Optional query: `activeOnly` (boolean, default `false`).

## Response

`200 OK`

```json
[
  {
    "id": "guid",
    "objectiveId": "guid",
    "name": "Sprint 1",
    "startDate": "2026-08-01",
    "endDate": "2026-08-14",
    "status": "active",
    "completedAt": null,
    "achievedAt": null
  }
]
```

`status` is one of `future`, `active`, `complete`, `incomplete`, `achieved`.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or has neither `projects:read`/`*` nor an active membership on this milestone or an ancestor of it |
| `404` | Objective doesn't exist in tenant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`GetByObjective`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetObjectiveSprints/GetObjectiveSprintsQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-1-fix-sprint-and-task-authorization.md`
