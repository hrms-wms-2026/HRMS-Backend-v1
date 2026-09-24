# Approve Task Creation Request

**POST** `/api/v1/work/task-creation-requests/{id}/approve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond `TenantPolicy` — in-handler checks require the caller to be the Objective's current owner.
**Idempotent:** No — a pending request can be approved once; a second call returns `409`.

## Description

Objective owner approves a pending task-creation request. Re-checks remaining slack at decision time (not at request-creation time). On success, creates the `WorkTask` from the stored payload (project-prefixed `shortId`) and marks the request `approved`.

## Request

Empty body.

## Response

`201 Created` — same `WorkTask` shape as Create Task.

```json
{
  "id": "guid",
  "objectiveId": "guid",
  "shortId": "WEB-7",
  "title": "Build the login page",
  "description": "optional",
  "categoryId": "guid",
  "statusId": "guid",
  "priority": "medium",
  "storyPoints": 5,
  "dueDate": "2026-09-01",
  "estimatedHours": 8,
  "completedHours": 0,
  "progressPercent": 0
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not an effective manager of this milestone (its owner, an active member, or the owner/an active member of any ancestor milestone) |
| `404` | Request, Objective, Category, or Project not found / inactive |
| `409` | Request already decided, or `estimatedHours` exceeds remaining slack (`InsufficientAllocationResponse`) |
| `422` | No task statuses configured for this milestone yet |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`ApproveRequest`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-2-task-creation-requests.md`
