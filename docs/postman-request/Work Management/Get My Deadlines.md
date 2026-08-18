# Get My Deadlines

**GET** `/api/v1/work/my-deadlines?from=YYYY-MM-DD&to=YYYY-MM-DD`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond `TenantPolicy` — returns the caller's own owned-objective end dates and assigned-task due dates only.

## Description

Read-only calendar-integration surface for Work Management (spec §7). Returns objectives the caller owns with `EndDate` in `[from, to]`, and tasks currently assigned to the caller with `DueDate` in that range. Does not write to Calendar tables.

## Query parameters

| Name | Required | Description |
|---|---|---|
| `from` | yes | Inclusive range start (`DateOnly`) |
| `to` | yes | Inclusive range end (`DateOnly`) |

## Response

`200 OK`

```json
{
  "objectiveDeadlines": [
    { "objectiveId": "guid", "title": "Milestone A", "endDate": "2026-08-15" }
  ],
  "taskDeadlines": [
    { "taskId": "guid", "shortId": "T-1", "title": "Task A", "dueDate": "2026-08-20" }
  ]
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or no employee record for the current user |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyDeadlines/`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-5-my-deadlines.md`
