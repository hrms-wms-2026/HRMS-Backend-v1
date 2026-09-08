**POST** `/api/v1/work/calendar-events/{id}/close`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`

## Description

Archives the Event while preserving the Event and its membership rows as history. Archived Events are excluded from active calendar color joins. Closing an already archived Event is idempotent.

## Request

No body. `id` is the calendar-event path parameter.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, tenant context missing, or permission denied |
| `404` | Calendar Event not found |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/CloseCalendarEvent/CloseCalendarEventCommandHandler.cs`
Plan: `docs/superpowers/specs/next/2026-08-25-work-management-project-calendar-design.md`
