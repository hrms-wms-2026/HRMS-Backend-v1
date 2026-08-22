# Edit Sprint

**PATCH** `/api/v1/work/sprints/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be the owning Objective's current owner.
**Idempotent:** Yes — repeating the same body produces the same resulting state (subject to the frozen
check below).

## Description

Edits a Sprint's name and date range. A Sprint that has already ended (`status` is `Complete` or
`Achieved`) is frozen and can no longer be edited — call fails with `409`. Only the owning Objective's
owner may edit; no bypass path.

## Request

```json
{
  "name": "Sprint 1 (renamed)",
  "startDate": "2026-09-01",
  "endDate": "2026-09-18"
}
```

## Response

`200 OK`

```json
{
  "id": "b3f1...guid",
  "objectiveId": "a1e2...guid",
  "name": "Sprint 1 (renamed)",
  "startDate": "2026-09-01",
  "endDate": "2026-09-18",
  "status": "Active",
  "completedAt": null,
  "achievedAt": null
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | `endDate` is before `startDate` |
| `403` | Caller lacks `projects:access`, or is not an effective manager of the owning Objective (its owner, an active member, or the owner/an active member of any ancestor Objective) |
| `404` | Sprint or its Objective doesn't exist in tenant |
| `409` | Sprint status is already `Complete` or `Achieved` — frozen, can no longer be edited |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`Edit`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/EditSprint/EditSprintCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-6-postman-doc-gaps.md`
