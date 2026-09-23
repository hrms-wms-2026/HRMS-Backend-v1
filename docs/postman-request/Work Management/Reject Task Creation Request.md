# Reject Task Creation Request

**POST** `/api/v1/work/task-creation-requests/{id}/reject`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond `TenantPolicy` — in-handler checks require the caller to be the Objective's current owner.

## Description

Objective owner rejects a pending task-creation request. No task is created. A non-empty `comment` is required.

## Request

```json
{
  "comment": "Out of scope for this milestone."
}
```

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | Missing / empty comment |
| `403` | Not authenticated, no employee record, or caller is not an effective manager of this milestone (its owner, an active member, or the owner/an active member of any ancestor milestone) |
| `404` | Request or Objective not found |
| `409` | Request has already been decided |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`RejectRequest`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RejectTaskCreationRequest/RejectTaskCreationRequestCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-2-task-creation-requests.md`
