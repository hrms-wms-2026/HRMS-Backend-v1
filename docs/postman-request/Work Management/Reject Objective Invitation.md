# Reject Objective Invitation

**POST** `/api/v1/work/objectives/invitations/{invitationId}/reject`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`.
**Permission:** no `[RequirePermission("projects:access")]` — the invitee may not have that permission yet. Caller must be the invited employee (resolved from session UserId → EmployeeId).

## Description

Rejects a pending invitation (`status: declined`). Caller must equal `invitedEmployeeId`. No membership or headship side effects — for a leader invite, the current head remains head.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | Milestone is achieved |
| `403` | Caller is not the invited employee, or has no Employee record |
| `404` | Invitation doesn't exist in tenant |
| `409` | Invitation has already been decided |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`RejectInvitation`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/RejectObjectiveInvitation/RejectObjectiveInvitationCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 8)
