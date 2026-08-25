# Add Project Member

**POST** `/api/v1/work/projects/{id}/members`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be this project's owner (`Project.LeadId` equals the caller's employee id).
**Idempotent:** No `Idempotency-Key` attribute (matches `POST /work/objectives/{id}/members`).

## Description

Invites an employee to become a project member **without naming a non-default Objective**. The handler loads the project's Default Objective (`Objective.IsDefault == true`) server-side and creates a normal pending `ProjectMemberInvitation` against that milestone. The invitee accepts or rejects through the existing invitation endpoints. A successful invite also enqueues an in-app `work_project_member_invited` notification via the Outbox (same transaction as the invitation row).

Does **not** add a `project_members` row immediately — the invitee must accept (`POST /api/v1/work/objectives/invitations/{id}/accept`). Already-active members of the Default Objective are a no-op (`alreadyMember: true`). Duplicate pending invitations for the same employee on the Default Objective return `409`.

`inviteType` on the created invitation is always `member`.

## Request

```json
{ "employeeId": "guid" }
```

The value is an `employees.id`. `{id}` is the project's Guid.

## Response

`204` if the employee is already an active member of the project's Default Objective:

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

`objectiveId` is the project's Default Objective id. `invitedById` is the caller's Employee id. The wrapper body is returned on both `204` and `202` so clients can read `alreadyMember` without a separate GET.

## Errors

| Status | Cause |
|---|---|
| `400` | Default Objective is achieved (frozen), the employee isn't an active employee in this tenant, or the project has no Default Objective |
| `403` | Caller lacks `projects:access`, has no Employee record, or is not this project's owner (`LeadId`) |
| `404` | Project doesn't exist in tenant, or is inactive |
| `409` | A pending invitation already exists for this employee on the Default Objective |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`AddMember`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AddProjectMember/AddProjectMemberCommandHandler.cs`
Request: `src/ONEVO.Api/Contracts/WorkManagement/Projects/AddProjectMemberRequest.cs`
Outcome mapping: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs` (`AddObjectiveMemberOutcomeResponse.ToViewModel`)
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-project-page-redesign/part-2-add-project-member.md`
