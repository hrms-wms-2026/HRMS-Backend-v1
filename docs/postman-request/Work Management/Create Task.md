# Create Task

**POST** `/api/v1/work/objectives/{objectiveId}/tasks`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** No — each successful call creates a new task.

## Description

Objective-owner direct create. Blocked with `409` when `estimatedHours` exceeds remaining slack on the Objective (`allocated_hours - child allocated hours - existing task estimated hours`). Non-owners must use the task-creation-request flow instead.

If the Objective is a whole-module member of one or more active calendar Events, `dueDate` becomes **required** and must fall inside every such Event's `[startDate, endDate]` window — otherwise `409`.

## Request

```json
{
  "title": "Build the login page",
  "description": "optional",
  "categoryId": "guid",
  "priority": "medium",
  "dueDate": "2026-09-01",
  "estimatedHours": 8,
  "storyPoints": 5,
  "sprintId": null
}
```

Optional, for the Planner create popover: `assigneeEmployeeId` (must be the caller), `planStartAt` and `planEndAt` (both or neither; end after start). They matter only when the create goes for approval (`202`): once approved, the task is assigned to the requester and a Planner block is added at that slot (if the slot can still be planned). A direct create (`201`) ignores them. Otherwise `400 invalid_plan_request`.

`categoryId` must be the id of a `TaskCategory` row belonging to this task's Project (categories are seeded per-Project; a dedicated listing endpoint is planned separately). `priority` is one of `low`, `medium`, `high`, `critical`. `estimatedHours`, `dueDate`, `description`, `storyPoints`, and `sprintId` are optional. Omit `sprintId` (or send `null`) to create a direct task under the Objective with no Sprint. When `sprintId` is provided, the Sprint must belong to this Objective and must not be Achieved.

## Response

`201 Created`

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
  "progressPercent": 0,
  "sprintId": null,
  "activeEventId": null,
  "activeEventName": null
}
```

`activeEventId` / `activeEventName` are set when the task is directly linked to an active calendar Event.

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure (title, categoryId, priority, negative hours); `invalid_plan_request`: `assigneeEmployeeId` is not the caller, or `planStartAt`/`planEndAt` is half-given or ends before it starts |
| `403` | Not authenticated, no employee record, or caller is not an effective manager of the Objective (its owner, an active member, or the owner/an active member of any ancestor Objective) — non-cascaded, non-owner members must submit a task creation request instead |
| `404` | Objective, Project, Category, or (when provided) Sprint not found / inactive |
| `409` | `estimatedHours` exceeds remaining slack; body is `InsufficientAllocationResponse` (`availableSlackHours`, `suggestedAction: "extend_allocation"`). Also returned when the provided Sprint is Achieved (frozen), or when the Objective is in an active Event and `dueDate` is missing or outside the Event window. |
| `422` | No task statuses configured for this milestone yet |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-1-schema-and-crud.md`
