# Delete Objective

**DELETE** `/api/v1/work/objectives/{id}`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head.

## Description

Soft-deletes a milestone (no cascade to descendants — design §4). Applies immediately if the caller created this Objective themselves; otherwise becomes a pending request routed to the Reporting Manager. `400` if `{id}` is the Default Objective — delete it via `DELETE /api/v1/work/projects/{id}` instead.

## Response

`204 No Content` (applied immediately) or `202 Accepted` with the pending `ObjectiveChangeRequest` body (same shape as Edit's pending response, `requestType: "delete"`, `payloadJson: null`).

**Breaking change (2026-08-14):** `requestedById`, `reportingManagerId`, and `decidedById` on the pending-request body now carry `employees.id` values, not `users.id`. Field names are unchanged. Clients that were caching or comparing against the old UserId-space value must re-fetch.

## Errors

| Status | Cause |
|---|---|
| `400` | `{id}` is the Default Objective |
| `403` | Caller is not an effective manager of `{id}` (its Head, an active member, or the Head/an active member of any ancestor Objective) |
| `404` | Objective doesn't exist in tenant |
| `409` | A change request is already pending for this objective |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Delete`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjective/DeleteObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
