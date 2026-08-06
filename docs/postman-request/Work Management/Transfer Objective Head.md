# Transfer Objective Head

**POST** `/api/v1/work/objectives/{id}/transfer`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head.

## Description

Reassigns a milestone's Head. Same immediate-vs-pending split as Delete: applies immediately if the caller created the Objective, otherwise routes to the Reporting Manager for approval. `ReportingManagerId` is never changed by a transfer, regardless of how many times headship moves (design §6). `400` if `{id}` is the Default Objective — its head is permanently the Project Lead.

## Request

```json
{ "newHeadUserId": "guid" }
```

## Response

`204 No Content` (applied immediately) or `202 Accepted` with the pending `ObjectiveChangeRequest` body (`requestType: "transfer"`).

## Errors

| Status | Cause |
|---|---|
| `400` | Missing `newHeadUserId`, or `{id}` is the Default Objective |
| `403` | Caller is not `{id}`'s current Head |
| `404` | Objective doesn't exist in tenant |
| `409` | A change request is already pending for this objective |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Transfer`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
