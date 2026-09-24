**PATCH** `/api/v1/work/calendar-events/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`

## Description

Updates an active Event's name, color, date window, and/or membership. When
`objectiveIds` (whole-module links) or `taskIds` (individual task links) is supplied it
replaces that membership set; `[]` clears it; omitting it keeps the current set. A
date-only edit re-validates the current members against the new window. Module dates
are never changed.

## Request

`id` is the calendar-event path parameter. All body fields are optional, but at least one must be supplied.

```json
{
  "name": "Q3 Launch Updated",
  "color": "#27AE60",
  "startDate": "2026-03-05",
  "endDate": "2026-04-05",
  "objectiveIds": ["objective-guid-1"],
  "taskIds": []
}
```

## Response

`200 OK` with the updated Event (`startDate`, `endDate`, `objectiveIds`, `taskIds`, ...).

## Errors

| Status | Cause |
|---|---|
| `400` | Empty payload, invalid name/color, `endDate` before `startDate`, or archived Event |
| `403` | Not authenticated, tenant context missing, or permission denied |
| `404` | Event not found, or a selected Objective/Task is not in the Event's project |
| `409` | A member task's due date falls outside the (new) window - including a window narrowed by this edit (R2/R3), or a newly-added task already belongs to another active Event (R1) |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/UpdateCalendarEvent/UpdateCalendarEventCommandHandler.cs`
Spec: `docs/superpowers/specs/next/2026-09-02-work-management-event-duration-and-hybrid-membership-design.md`
