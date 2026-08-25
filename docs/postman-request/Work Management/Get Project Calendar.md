**GET** `/api/v1/work/projects/{projectId}/calendar`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`

## Description

Returns every Objective in the project for the project-wide calendar. `canEdit` is false for callers without effective-manager access, achieved Objectives, and the project Default Objective. Active calendar-event membership supplies `calendarEventId` and `calendarEventColor`.

## Request

No body. `projectId` is a path parameter.

## Response

`200 OK`

```json
[
  {
    "objectiveId": "guid",
    "projectId": "guid",
    "parentObjectiveId": "guid|null",
    "title": "Design Phase",
    "startDate": "2026-01-01",
    "endDate": "2026-03-01",
    "isActive": true,
    "isAchieved": false,
    "canEdit": true,
    "calendarEventId": "guid|null",
    "calendarEventColor": "#RRGGBB|null"
  }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, tenant context missing, or permission denied |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetProjectCalendar/GetProjectCalendarQueryHandler.cs`
Plan: `docs/superpowers/specs/next/2026-08-25-work-management-project-calendar-design.md`
