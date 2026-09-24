# Cancel Task Creation Request

**POST** `/api/v1/work/task-creation-requests/{id}/cancel`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond `TenantPolicy` — in-handler checks require the caller to be the original requester.

## Description

The requester cancels their own still-pending task-creation request. Owners decide via approve/reject instead.

## Request

Empty body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no employee record, or caller is not the requester |
| `404` | Request not found |
| `409` | Request has already been decided |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`CancelRequest`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CancelTaskCreationRequest/CancelTaskCreationRequestCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-2-task-creation-requests.md`
