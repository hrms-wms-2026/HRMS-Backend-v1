# Approve Objective Change Request

**POST** `/api/v1/work/objectives/change-requests/{requestId}/approve`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must equal the request's `reportingManagerId`.

## Description

Approves a pending Delete/Edit/Transfer request. Applies the underlying action (soft-delete, field update, or head reassignment) and marks the request `approved` in one transaction — no separate action by the original requester.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller is not this request's Reporting Manager |
| `404` | Request or its target Objective doesn't exist in tenant |
| `409` | Request has already been decided |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`ApproveChangeRequest`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/ApproveObjectiveChangeRequestCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
