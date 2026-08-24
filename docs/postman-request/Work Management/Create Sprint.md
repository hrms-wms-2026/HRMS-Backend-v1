# Create Sprint

**POST** `/api/v1/work/objectives/{objectiveId}/sprints`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone (Objective)'s current owner.
**Idempotent:** No — each call creates a new Sprint.

## Description

Creates a Sprint under an Objective. `Status` is computed automatically, not caller-supplied: `Active` if
`startDate <= today`, otherwise `Future`. Only the Objective's owner may create Sprints under it — there is
no tenant-permission-bypass path for this action, unlike some read endpoints in this module.

## Request

```json
{
  "name": "Sprint 1",
  "startDate": "2026-09-01",
  "endDate": "2026-09-14"
}
```

## Response

`201 Created`

```json
{
  "id": "b3f1...guid",
  "objectiveId": "a1e2...guid",
  "name": "Sprint 1",
  "startDate": "2026-09-01",
  "endDate": "2026-09-14",
  "status": "Future",
  "completedAt": null,
  "achievedAt": null
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | `endDate` is before `startDate` |
| `403` | Caller lacks `projects:access`, or is not an effective manager of this milestone (its owner, an active member, or the owner/an active member of any ancestor milestone) |
| `404` | Objective doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`Create`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint/CreateSprintCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-6-postman-doc-gaps.md`
