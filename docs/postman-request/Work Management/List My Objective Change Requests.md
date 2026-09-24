# List My Objective Change Requests

**GET** `/api/v1/work/objectives/change-requests/mine`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access`.

## Description

The caller's approval queue — every `pending` change request where the caller is the Reporting Manager, oldest first.

## Response

`200 OK` — a JSON array of `ObjectiveChangeRequest` objects (same shape as Edit's pending response).

**Breaking change (2026-08-14):** `requestedById`, `reportingManagerId`, and `decidedById` now carry `employees.id` values, not `users.id`. Field names are unchanged. Clients that were caching or comparing against the old UserId-space value must re-fetch.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`ListMyChangeRequests`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Queries/ListMyObjectiveChangeRequests/ListMyObjectiveChangeRequestsQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
