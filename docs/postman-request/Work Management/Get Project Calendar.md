**GET** `/api/v1/work/projects/{projectId}/calendar`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`

## Description

Returns the project calendar as `{ modules, bands }`. `modules` is one row per Objective
(module dates unchanged) plus its `events` array: a `"whole"` link when the module itself
is an event member (`tasksInEventCount` == the module's task total), or a `"partial"` link
when only some of its tasks are directly linked (`tasksInEventCount` == that count).
`bands` is one entry per active Event with its date window; `canEdit` on a band is true
when the caller is an effective manager of any Objective contributing to the Event.

## Request

No body. `projectId` is a path parameter.

## Response

`200 OK`

```json
{
  "modules": [
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
      "events": [
        {
          "eventId": "guid",
          "eventName": "Q3 Launch",
          "eventColor": "#RRGGBB",
          "eventStartDate": "2026-03-01",
          "eventEndDate": "2026-03-31",
          "membership": "whole|partial",
          "tasksInEventCount": 3,
          "taskTotalCount": 5
        }
      ]
    }
  ],
  "bands": [
    {
      "eventId": "guid",
      "name": "Q3 Launch",
      "color": "#RRGGBB",
      "startDate": "2026-03-01",
      "endDate": "2026-03-31",
      "canEdit": true
    }
  ]
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, tenant context missing, or permission denied |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetProjectCalendar/GetProjectCalendarQueryHandler.cs`
Spec: `docs/superpowers/specs/next/2026-09-02-work-management-event-duration-and-hybrid-membership-design.md`
