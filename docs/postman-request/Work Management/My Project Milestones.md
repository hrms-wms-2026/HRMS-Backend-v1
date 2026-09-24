# My Project Milestones

**GET** `/api/v1/work/projects/{projectId}/objectives/mine`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` (module base gate only — this endpoint can only ever return the caller's own rows, so an unrelated `projectId` just yields an empty array, never `403`/`404` beyond the base permission check).

## Description

Every milestone in the given project the caller has ever had a `project_members` row for, at any status (active, removed, or transferred-away) — the frontend is expected to filter by `membershipIsActive`/`objectiveIsActive`/`isAchieved` as needed; the API does not pre-filter to active-only. Each milestone's current Head (`ownerId`) and Reporting Manager (`reportingManagerId`) names are resolved server-side as `ownerName`/`reportingManagerName` (`First Last`, from the matching `Employee` record). `reportingManagerId`/`reportingManagerName` are `null` for the Default Objective (it has no Reporting Manager). A nonexistent or inaccessible `projectId` returns `200` with an empty array, never `404`. `isOwner` is `true` when the caller is this milestone's current Head — computed server-side the same way `Project.isLead` is, so the frontend never needs its own user id.

## Response

`200 OK`

```json
[
  {
    "objectiveId": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false,
    "title": "string", "ownerId": "guid", "ownerName": "string|null",
    "reportingManagerId": "guid|null", "reportingManagerName": "string|null",
    "startDate": "date", "endDate": "date", "allocatedHours": "decimal", "completedHours": "decimal",
    "objectiveIsActive": true, "isAchieved": false, "achievedAt": "datetime|null",
    "membershipIsActive": true, "membershipRemovedAt": "datetime|null", "isOwner": true
  }
]
```

**Breaking change (2026-08-14):** `ownerId` and `reportingManagerId` now carry `employees.id` values, not `users.id`. The field names are unchanged. Clients that were caching or comparing against the old UserId-space value must re-fetch.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetMine`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyProjectMilestones/GetMyProjectMilestonesQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-08-work-management-my-project-milestones.md`
Design: `docs/superpowers/specs/next/2026-08-08-work-management-my-project-milestones-design.md`
