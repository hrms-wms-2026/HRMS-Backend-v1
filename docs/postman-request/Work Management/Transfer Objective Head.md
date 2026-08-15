# Transfer Objective Head

**POST** `/api/v1/work/objectives/{id}/transfer`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head.

## Description

Reassigns a milestone's Head. Same immediate-vs-pending split as Delete: applies immediately if the caller created the Objective, otherwise routes to the Reporting Manager for approval. `400` if `{id}` is the Default Objective — its head is permanently the Project Lead.

Applying a transfer (immediately or via approval) also syncs project membership for both heads, cascades `ReportingManagerId` to the milestone's direct children, and drops the old head's project participation if they have no other active access.

## Request

```json
{ "newHeadEmployeeId": "guid" }
```

**Breaking change (2026-08-14):** request body field renamed from `newHeadUserId` to `newHeadEmployeeId`. The value is an `employees.id`.

## Response

`204 No Content` (applied immediately) or `202 Accepted` with the pending `ObjectiveChangeRequest` body (`requestType: "transfer"`).

**Breaking change (2026-08-14):** `requestedById`, `reportingManagerId`, and `decidedById` on the pending-request body now carry Employee ids. Field names are unchanged. Clients that were caching or comparing against the old UserId-space value must re-fetch.

## Errors

| Status | Cause |
|---|---|
| `400` | Missing `newHeadEmployeeId`, `{id}` is the Default Objective, the milestone is achieved, or the new head isn't an active employee in this tenant |
| `403` | Caller is not `{id}`'s current Head |
| `404` | Objective doesn't exist in tenant |
| `409` | A change request is already pending for this objective |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Transfer`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`, `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 19/25)
