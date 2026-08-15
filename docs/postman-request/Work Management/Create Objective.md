# Create Objective

**POST** `/api/v1/work/objectives`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be the parent Objective's current Head (Employee id).

## Description

Creates a sub-milestone under an existing Objective. The **creator is always the starting owner** and the first membership row. `headEmployeeId`, if set and different from the creator, queues a pending **leader** invitation — it does **not** assign headship immediately. `memberInvitations` queues pending **member** invitations the same way.

Rejected with `400` if the new milestone's date range or allocated hours would fall outside the parent's.

Also syncs project membership for the creator and auto-grants `projects:access` to the creator (takes effect on their next login).

## Request

```json
{
  "parentObjectiveId": "guid",
  "title": "Design Phase",
  "description": "optional",
  "startDate": "2026-01-15",
  "endDate": "2026-03-01",
  "allocatedHours": 20,
  "headEmployeeId": "guid|null",
  "memberInvitations": [{ "employeeId": "guid", "type": "member" }]
}
```

**Breaking change (2026-08-15):** request field renamed from `headUserId` to `headEmployeeId`. The value is an `employees.id`. Optional `memberInvitations` added.

## Response

`201 Created`

```json
{
  "id": "guid", "projectId": "guid", "parentObjectiveId": "guid", "isDefault": false,
  "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid",
  "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": null,
  "allocatedHours": 20, "completedHours": 0, "isActive": true, "isAchieved": false,
  "achievedAt": null, "createdAt": "datetime", "updatedAt": null,
  "ownerName": "string|null", "reportingManagerName": "string|null", "isOwner": true
}
```

`ownerId` and `reportingManagerId` are the creator's Employee id. Pending invitations are not included in this body — they appear on Get Objective Members / My Objective Invitations.

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure, date range/hours would exceed the parent's, creator isn't an active employee, or a proposed head/member isn't an active employee |
| `403` | Caller is not the parent Objective's current Head, or has no Employee record |
| `404` | Parent Objective doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Create`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 11)
