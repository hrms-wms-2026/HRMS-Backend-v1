# Achieve Objective

**POST** `/api/v1/work/objectives/{id}/achieve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head.
**Idempotent:** No — a second call on an already-achieved milestone returns `409`.

## Description

Marks a milestone Achieved (completion state, independent of soft-delete). Every direct sub-milestone must already be Achieved first. Same immediate-vs-pending split as Delete: applies immediately if the caller created this milestone, otherwise creates a pending `achieve` change request routed to the Reporting Manager. Once applied, the milestone is frozen (Edit/Transfer/member-management all return `400`) and the Head's active project participation is dropped unless they have another reason to stay (another milestone, or a direct membership) - see `GET /objectives/mine/history` for what happens to their access.

## Request

No body.

## Response

`204 No Content` (applied immediately) or `202 Accepted` with the created change request (pending approval).

**Breaking change (2026-08-14):** `requestedById` and `reportingManagerId` on the pending-request body now carry `employees.id` values, not `users.id`. Field names are unchanged. Clients that were caching or comparing against the old UserId-space value must re-fetch.

## Errors

| Status | Cause |
|---|---|
| `400` | Target is the Default Objective (use the Project achieve endpoint), or a direct sub-milestone isn't yet Achieved |
| `403` | Caller lacks `projects:access`, or is not this milestone's Head |
| `404` | Milestone doesn't exist in tenant, or is inactive |
| `409` | Already achieved, or a change request is already pending for this milestone |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Achieve`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective/AchieveObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
