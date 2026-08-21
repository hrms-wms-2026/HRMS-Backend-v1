# Achieve Sprint

**POST** `/api/v1/work/sprints/{id}/achieve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be the owning Objective's current owner.
**Idempotent:** No — a second call on an already-achieved Sprint returns `409`.

## Description

Marks a Sprint Achieved. Distinct from Complete (below) — Achieved has no task-status precondition, it's a
direct owner action. Every active member of the owning Objective is notified synchronously
(`work_sprint_achieved` template — this predates the Outbox pattern used for the newer Project-member
notifications; it's a direct `INotificationDispatcher.SendTemplatedAsync` call inside the same transaction).

## Request

No body.

## Response

`200 OK`

```json
{
  "id": "b3f1...guid",
  "objectiveId": "a1e2...guid",
  "name": "Sprint 1",
  "startDate": "2026-09-01",
  "endDate": "2026-09-14",
  "status": "Achieved",
  "completedAt": null,
  "achievedAt": "2026-09-15T10:00:00Z"
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or is not the owning Objective's owner |
| `404` | Sprint or its Objective doesn't exist in tenant |
| `409` | Sprint is already Achieved |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`Achieve`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/AchieveSprint/AchieveSprintCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-6-postman-doc-gaps.md`
