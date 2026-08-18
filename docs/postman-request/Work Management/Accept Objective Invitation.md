# Accept Objective Invitation

**POST** `/api/v1/work/objectives/invitations/{invitationId}/accept`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`.
**Permission:** no `[RequirePermission("projects:access")]` — the invitee may not have that permission yet. Caller must be the invited employee (resolved from session UserId → EmployeeId).

## Description

Accepts a pending invitation. Caller must equal `invitedEmployeeId`.

- `inviteType: member` — upserts a `project_members` row for this milestone. Does not grant `projects:access`.
- `inviteType: leader` — reassigns `ownerId` to the invitee, cascades `reportingManagerId` to direct children, upserts the invitee's membership, deactivates the old head's membership on this milestone, and auto-grants `projects:access` to the invitee's User.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | Milestone is achieved, or the invited employee is no longer active |
| `403` | Caller is not the invited employee, or has no Employee record |
| `404` | Invitation or its target Objective doesn't exist in tenant |
| `409` | Invitation has already been decided |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`AcceptInvitation`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/AcceptObjectiveInvitation/AcceptObjectiveInvitationCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 7)
