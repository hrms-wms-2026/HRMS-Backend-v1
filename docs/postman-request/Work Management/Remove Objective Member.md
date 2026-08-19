# Remove Objective Member

**DELETE** `/api/v1/work/objectives/{id}/members/{employeeId}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head (Employee id).

## Description

If `{employeeId}` has an active `project_members` row on this milestone, deactivates it. If they have no active membership but a pending invitation on this milestone, cancels that invitation (`status: cancelled`). Removing the milestone's current Head is rejected — use Transfer Objective Head instead.

## Request

No body. `{employeeId}` is an `employees.id`.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | Milestone is achieved (frozen), or `employeeId` is this milestone's current head |
| `403` | Caller lacks `projects:access`, has no Employee record, or is not this milestone's Head |
| `404` | Milestone doesn't exist / is inactive, **or** this employee has neither an active membership nor a pending invitation on this milestone |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`RemoveMember`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/RemoveObjectiveMember/RemoveObjectiveMemberCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 5)
