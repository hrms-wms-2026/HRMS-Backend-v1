**POST** `/api/v1/work/projects/{projectId}/calendar-events`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`

## Description

Creates an active calendar Event with a date window and a hybrid membership: whole
Modules (`objectiveIds` - a live link that always reflects the module's current tasks)
and/or individual Tasks (`taskIds`). A Module may be a whole-member of many active
Events, but a Task belongs to at most one active Event. Every member task's due date
must fall inside `[startDate, endDate]`. Module dates are never changed.

## Request

`projectId` is a path parameter.

```json
{
  "name": "Q3 Launch",
  "color": "#2F80ED",
  "startDate": "2026-03-01",
  "endDate": "2026-03-31",
  "objectiveIds": ["objective-guid-1"],
  "taskIds": ["task-guid-1", "task-guid-2"]
}
```

## Response

`201 Created` with the created Event: `id`, `projectId`, `name`, `color`, `status`,
`startDate`, `endDate`, `objectiveIds`, `taskIds`, `createdAt`.

## Errors

| Status | Cause |
|---|---|
| `400` | Invalid name/color, `endDate` before `startDate`, or malformed payload |
| `403` | Not authenticated, tenant context missing, or permission denied |
| `404` | Project, a selected Objective, or a selected Task is not found in the project |
| `409` | A member task has no due date or a due date outside `[startDate, endDate]` (R2), or a member task already belongs to another active Event (R1) |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/CreateCalendarEvent/CreateCalendarEventCommandHandler.cs`
Spec: `docs/superpowers/specs/next/2026-09-02-work-management-event-duration-and-hybrid-membership-design.md`
