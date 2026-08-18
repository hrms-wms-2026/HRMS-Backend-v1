# Request Allocation Extension

**POST** `/api/v1/work/objectives/{id}/allocation-requests`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be the Objective owner.
**Idempotent:** No — blocked by the existing one-pending-change-request-per-objective unique index (`409` if another pending request already exists).

## Description

Objective owner requests `+N` allocated hours. Creates an `objective_change_requests` row with `requestType: "extend_allocation"`, routed to the Objective's current Reporting Manager (parent owner). Slack is **not** checked at submit time.

On approve (`POST /api/v1/work/objectives/change-requests/{requestId}/approve`):

- If the approver's own slack ≥ N, the **child** `allocatedHours` increases by N. The approver's own `allocatedHours` is unchanged (the hours come out of existing slack).
- If the approver's own slack < N, the approve call returns `409` and the original request stays `pending`. The approver must first submit their own allocation-extension (same endpoint, on their own Objective) up the chain, then return to approve.

**Root case:** an Objective with no Reporting Manager (the Project's Default Objective) cannot use this endpoint (`400`). The Project lead edits allocated hours directly via `PUT /api/v1/work/projects/{id}` (`allocatedHours`).

## Worked example (spec §4)

Child Objective currently has 60 allocated hours and asks for **+20**. Approver's own Objective has 100 allocated, children summing to 60, slack **40** — approve succeeds; child becomes 80; approver stays 100.

If the approver's children instead sum to 90 (slack **10**), approve returns `409`. Approver requests +20 (or more) on their own Objective first; after that is approved, they retry the still-pending child request.

## Request

```json
{
  "requestedAdditionalHours": 20,
  "reason": "Need more hours for the new scope"
}
```

`requestedAdditionalHours` must be > 0. `reason` is required.

## Response

`202 Accepted` — `ObjectiveChangeRequest` (same shape as other pending change requests).

```json
{
  "id": "guid",
  "objectiveId": "guid",
  "requestType": "extend_allocation",
  "requestedById": "guid",
  "reportingManagerId": "guid",
  "status": "pending",
  "payloadJson": "{\"RequestedAdditionalHours\":20,\"Reason\":\"Need more hours for the new scope\"}",
  "decidedAt": null,
  "decidedById": null,
  "createdAt": "2026-08-17T00:00:00+00:00"
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure, or this is a top-level milestone with no Reporting Manager |
| `403` | Not the Objective owner, or no employee record |
| `404` | Objective not found / inactive |
| `409` | Another change request is already pending for this Objective |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`RequestAllocationExtension`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RequestAllocationExtension/RequestAllocationExtensionCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-3-allocation-extend-cascade.md`
