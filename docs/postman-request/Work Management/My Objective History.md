# My Objective History

**GET** `/api/v1/work/objectives/mine/history`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`.

## Description

Milestones the caller used to have active access to (as Head or member) but no longer does - because they were Transferred away, removed as a member, or the milestone was Achieved and they had no other reason to stay in the project. Read-only; no write actions are available from this view.

## Response

`200 OK`

```json
[
  { "objectiveId": "guid", "title": "string", "projectId": "guid", "isAchieved": true, "removedAt": "datetime" }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`MyHistory`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyObjectiveHistory/GetMyObjectiveHistoryQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
