# Add Objective Member

**POST** `/api/v1/work/objectives/{id}/members`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this milestone's current Head.
**Idempotent:** Yes in effect — adding an already-active member is a no-op (204).

## Description

Adds an employee to this milestone's project membership (`project_members`, scoped to this Objective's id). The target must be an active employee in this tenant. Does NOT grant `projects:access` — only assigning someone as Head does that (see Create/Transfer Objective Head).

This is still the **direct synchronous add** (204). The invite/accept workflow (pending invitation, `202 Accepted`) is backend Tasks 1–12 and is not implemented yet.

## Request

```json
{ "employeeId": "guid" }
```

**Breaking change (2026-08-14):** request body field renamed from `userId` to `employeeId`. The value is an `employees.id`.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | Milestone is achieved (frozen), or the employee isn't an active employee in this tenant |
| `403` | Caller lacks `projects:access`, or is not this milestone's Head |
| `404` | Milestone doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`AddMember`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AddObjectiveMember/AddObjectiveMemberCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`, `docs/superpowers/plans/next/2026-08-14-work-management-objective-member-management.md` (Task 22/25)
