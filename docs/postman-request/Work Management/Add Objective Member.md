# Add Objective Member

**POST** `/api/v1/work/objectives/{id}/members`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head (Employee id).

## Description

Invites an employee to this milestone. Does **not** add a `project_members` row immediately — the invitee must accept (`POST .../invitations/{id}/accept`). Already-active members are a no-op. Duplicate pending invitations for the same employee on this milestone return `409`.

`inviteType` on the created invitation is always `member`. Does NOT grant `projects:access` (only accepting a **leader** invitation, or Create/Transfer applying headship, does that).

## Request

```json
{ "employeeId": "guid" }
```

The value is an `employees.id`.

## Response

`204` if the employee is already an active member of this milestone:

```json
{ "alreadyMember": true, "invitation": null }
```

`202` if a pending invitation was created:

```json
{
  "alreadyMember": false,
  "invitation": {
    "id": "guid", "projectId": "guid", "objectiveId": "guid",
    "invitedEmployeeId": "guid", "inviteType": "member", "status": "pending",
    "invitedById": "guid", "decidedAt": null, "createdAt": "datetime"
  }
}
```

`invitedById` is the caller's Employee id. The wrapper body is returned on both `204` and `202` so clients can read `alreadyMember` without a separate GET.

## Errors

| Status | Cause |
|---|---|
| `400` | Milestone is achieved (frozen), or the employee isn't an active employee in this tenant |
| `403` | Caller lacks `projects:access`, has no Employee record, or is not this milestone's Head |
| `404` | Milestone doesn't exist in tenant, or is inactive |
| `409` | A pending invitation already exists for this employee on this milestone |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`AddMember`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 4)
