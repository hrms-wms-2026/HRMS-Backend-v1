# Unachieve Objective

**POST** `/api/v1/work/objectives/{id}/unachieve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head.

## Description

Reverts an Achieved milestone back to active, unfreezing it. No precondition (always reversible). Same immediate-vs-pending split as Achieve. On applying, restores the Head's active project membership.

## Request

No body.

## Response

`204 No Content` (applied immediately) or `202 Accepted` with the created change request (pending approval).

## Errors

| Status | Cause |
|---|---|
| `400` | Target is the Default Objective, or the current head is no longer an active employee in this tenant |
| `403` | Caller lacks `projects:access`, or is not this milestone's Head |
| `404` | Milestone doesn't exist in tenant, or is inactive |
| `409` | Milestone is not achieved, or a change request is already pending |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Unachieve`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UnachieveObjective/UnachieveObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
