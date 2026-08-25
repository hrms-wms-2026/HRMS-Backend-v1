**PATCH** `/api/v1/work/calendar-events/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`

## Description

Updates an active Event's name, color, and/or Objective membership. When `objectiveIds` is supplied it replaces the active membership set. Objective dates are never changed.

## Request

`id` is the calendar-event path parameter. All body fields are optional, but at least one must be supplied.

```json
{
  "name": "Q3 Launch Updated",
  "color": "#27AE60",
  "objectiveIds": ["objective-guid-1", "objective-guid-3"]
}
```

## Response

`200 OK` with the updated Event and its Objective ids.

## Errors

| Status | Cause |
|---|---|
| `400` | Invalid or empty update payload, invalid name/color, or archived Event |
| `403` | Not authenticated, tenant context missing, or permission denied |
| `404` | Event not found or selected Objective is not found in the Event's project |
| `409` | One or more selected Objectives already belong to another active Event |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/UpdateCalendarEvent/UpdateCalendarEventCommandHandler.cs`
Plan: `docs/superpowers/specs/next/2026-08-25-work-management-project-calendar-design.md`
