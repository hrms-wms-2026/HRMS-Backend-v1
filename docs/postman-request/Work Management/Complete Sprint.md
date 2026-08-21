# Complete Sprint

**POST** `/api/v1/work/sprints/{id}/complete`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be the owning Objective's current owner.
**Idempotent:** No — a second call on an already-complete Sprint fails the precondition check below (its
tasks are already all in a complete-marking status, so the call would 422 rather than a clean `409`; treat
repeats as unsafe).

## Description

Marks a Sprint Complete. Unlike Achieve, this has a hard precondition: every Task currently in the Sprint
must be in a `TaskStatus` where `MarksTaskComplete == true` — a Sprint with any task still in progress
cannot be completed. Every active member of the owning Objective is notified synchronously
(`work_sprint_completed` template, same direct-dispatch pattern as Achieve — not Outbox-routed).

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
  "status": "Complete",
  "completedAt": "2026-09-14T18:00:00Z",
  "achievedAt": null
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or is not the owning Objective's owner |
| `404` | Sprint or its Objective doesn't exist in tenant |
| `422` | At least one Task in the Sprint is not yet in a complete-marking status |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`Complete`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/CompleteSprintCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-6-postman-doc-gaps.md`
