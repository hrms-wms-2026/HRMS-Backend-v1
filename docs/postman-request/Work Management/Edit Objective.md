# Edit Objective

**PUT** `/api/v1/work/objectives/{id}`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head.

## Description

Edits a milestone. A non-conflicting edit (within the parent's date/hours bounds) applies immediately. A conflicting edit applies immediately only if the caller is the milestone's own creator; otherwise it becomes a pending request routed to the milestone's Reporting Manager. `400` if `{id}` is a Default Objective — edit it via `PUT /api/v1/work/projects/{id}` instead.

## Request

```json
{ "title": "string", "description": "optional", "startDate": "date", "endDate": "date", "allocatedHours": 18 }
```

## Response

`200 OK` (applied immediately) — the updated Objective, same shape as Create's response.
`202 Accepted` (pending) — `{ "id": "guid", "objectiveId": "guid", "requestType": "edit", "requestedById": "guid", "reportingManagerId": "guid", "status": "pending", "payloadJson": "string", "decidedAt": null, "decidedById": null, "createdAt": "datetime" }`

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure, or `{id}` is the Default Objective |
| `403` | Caller is not `{id}`'s current Head |
| `404` | Objective or its parent doesn't exist in tenant |
| `409` | A change request is already pending for this objective |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`Edit`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/EditObjective/EditObjectiveCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
