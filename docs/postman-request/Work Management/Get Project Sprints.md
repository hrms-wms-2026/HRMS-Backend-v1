# Get Project Sprints

**GET** `/api/v1/work/projects/{projectId}/sprints`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:read` or active project-member objective access (handler enforces this).
**Idempotent:** Yes.

## Description

Returns every sprint belonging to an Objective under the requested Project. Callers with `projects:read` or `*`
see all project sprints; other callers see only sprints for Objectives returned by their active project membership.

## Request

No body. `projectId` is a path parameter.

## Response

`200 OK`

```json
[
  {
    "id": "guid",
    "objectiveId": "guid",
    "name": "Sprint 1",
    "startDate": "2026-08-01",
    "endDate": "2026-08-31",
    "status": "active",
    "completedAt": null,
    "achievedAt": null
  }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, missing tenant/employee context, or no accessible Objective membership |
| `404` | Project not found or inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetProjectSprints/GetProjectSprintsQueryHandler.cs`
Plan: `docs/superpowers/specs/next/2026-08-24-work-management-board-backlog-project-wide-design.md`
