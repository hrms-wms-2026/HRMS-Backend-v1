# Get Objective Members

**GET** `/api/v1/work/objectives/{id}/members`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:read`/`*` OR an active membership on this milestone or any of its ancestors — checked in-handler, not via `[RequirePermission]` (same pattern as Get Objective).

## Description

Returns this milestone's active members merged with pending invitations. Active rows have `pending: false`. Pending invitations have `pending: true`, `inviteType` (`member` or `leader`), and `invitationId`.

## Response

`200 OK`

```json
{
  "items": [
    {
      "employeeId": "guid",
      "isHead": true,
      "pending": false,
      "inviteType": null,
      "invitationId": null,
      "sinceOrInvitedAt": "datetime"
    },
    {
      "employeeId": "guid",
      "isHead": false,
      "pending": true,
      "inviteType": "member",
      "invitationId": "guid",
      "sinceOrInvitedAt": "datetime"
    }
  ]
}
```

`employeeId` is an `employees.id`. `isHead` is true when that employee is the milestone's current `ownerId`.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller has no Employee record, or has neither `projects:read`/`*` nor an active membership on this milestone or an ancestor of it |
| `404` | Milestone doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetMembers`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveMembers/GetObjectiveMembersQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 6)
