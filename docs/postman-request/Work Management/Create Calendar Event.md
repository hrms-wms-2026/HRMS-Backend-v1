**POST** `/api/v1/work/projects/{projectId}/calendar-events`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`

## Description

Creates an active visual calendar Event and assigns the selected project Objectives to its color. Grouping is non-destructive and does not change Objective dates. Each Objective can belong to at most one active Event.

## Request

`projectId` is a path parameter.

```json
{
  "name": "Q3 Launch",
  "color": "#2F80ED",
  "objectiveIds": ["objective-guid-1", "objective-guid-2"]
}
```

## Response

`201 Created` with the created Event and its Objective ids.

## Errors

| Status | Cause |
|---|---|
| `400` | Invalid name, color, or payload |
| `403` | Not authenticated, tenant context missing, or permission denied |
| `404` | Project or selected Objective is not found in the project |
| `409` | One or more selected Objectives already belong to another active Event |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/CreateCalendarEvent/CreateCalendarEventCommandHandler.cs`
Plan: `docs/superpowers/specs/next/2026-08-25-work-management-project-calendar-design.md`
