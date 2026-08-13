# Remove Objective Member

**DELETE** `/api/v1/work/objectives/{id}/members/{userId}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head.

## Description

Deactivates a user's membership on this milestone. Removing the milestone's current Head is rejected — use Transfer Objective Head instead, which handles the membership handoff correctly.

## Request

No body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | Milestone is achieved (frozen), or `userId` is this milestone's current head |
| `403` | Caller lacks `projects:access`, or is not this milestone's Head |
| `404` | Milestone doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`RemoveMember`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
