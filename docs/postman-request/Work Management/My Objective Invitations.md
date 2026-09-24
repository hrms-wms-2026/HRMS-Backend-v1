# My Objective Invitations

**GET** `/api/v1/work/objectives/invitations/mine`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`.
**Permission:** no `[RequirePermission("projects:access")]` — pending invitees may not have that permission yet. Returns only invitations addressed to the caller's Employee id.

## Description

The caller's own pending invitations across every objective they've been invited to.

## Response

`200 OK`

```json
[
  {
    "id": "guid",
    "projectId": "guid",
    "objectiveId": "guid",
    "invitedEmployeeId": "guid",
    "inviteType": "member",
    "status": "pending",
    "invitedById": "guid",
    "decidedAt": null,
    "createdAt": "datetime"
  }
]
```

`invitedEmployeeId` and `invitedById` are Employee ids. `inviteType` is `member` or `leader`.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller is not authenticated, tenant context is missing, or the caller has no Employee record |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`MyInvitations`)
Handler: `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Queries/GetMyObjectiveInvitations/GetMyObjectiveInvitationsQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 9)
