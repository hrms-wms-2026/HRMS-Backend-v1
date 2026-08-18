# Transfer Objective Head

**POST** `/api/v1/work/objectives/{id}/transfer`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head (Employee id).

## Description

Reassigns a milestone's Head by `newHeadEmployeeId` (`employees.id`). Three outcomes:

1. **Caller created the Objective** — applies immediately: `ownerId` becomes the new head's Employee id, membership is synced, `ReportingManagerId` cascades to direct children, `projects:access` is auto-granted to the new head's User.
2. **Caller did not create it, and the Objective has a Reporting Manager** — creates a pending `ObjectiveChangeRequest` (`requestType: "transfer"`) routed to that Reporting Manager. Unchanged from the RM-approval path.
3. **Caller did not create it, and the Objective has no Reporting Manager** — creates a pending **leader** invitation instead of a change request. Caller remains Head until the invitee accepts.

`400` if `{id}` is the Default Objective — its head is permanently the Project Lead.

## Request

```json
{ "newHeadEmployeeId": "guid" }
```

## Response

Wrapper body on both success statuses (so clients can read `applied` / `pendingInvitation`):

`204` applied immediately:

```json
{ "applied": true, "pendingChangeRequest": null, "pendingInvitation": null }
```

`202` pending change request (RM path):

```json
{
  "applied": false,
  "pendingChangeRequest": {
    "id": "guid", "objectiveId": "guid", "requestType": "transfer",
    "requestedById": "guid", "reportingManagerId": "guid", "status": "pending",
    "payloadJson": "{\"newHeadEmployeeId\":\"guid\"}", "decidedAt": null, "decidedById": null, "createdAt": "datetime"
  },
  "pendingInvitation": null
}
```

`202` pending leader invitation (no-RM path):

```json
{
  "applied": false,
  "pendingChangeRequest": null,
  "pendingInvitation": {
    "id": "guid", "projectId": "guid", "objectiveId": "guid",
    "invitedEmployeeId": "guid", "inviteType": "leader", "status": "pending",
    "invitedById": "guid", "decidedAt": null, "createdAt": "datetime"
  }
}
```

`requestedById`, `reportingManagerId`, `invitedById`, and `invitedEmployeeId` are Employee ids.

## Errors

| Status | Cause |
|---|---|
| `400` | `{id}` is the Default Objective, the milestone is achieved, or the new head isn't an active employee in this tenant |
| `403` | Caller is not `{id}`'s current Head, or has no Employee record |
| `404` | Objective doesn't exist in tenant, or is inactive |
| `409` | A change request is already pending (RM path), or a leader invitation is already pending (no-RM path) |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Transfer`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 10)
