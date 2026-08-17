# Reject Objective Change Request

**POST** `/api/v1/work/objectives/change-requests/{requestId}/reject`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must equal the request's `reportingManagerId`.

## Description

Rejects a pending request. The target Objective is left unchanged; the request row is kept with `status: "rejected"` for history, not deleted.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller is not this request's Reporting Manager |
| `404` | Request doesn't exist in tenant |
| `409` | Request has already been decided |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`RejectChangeRequest`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/RejectObjectiveChangeRequest/RejectObjectiveChangeRequestCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
