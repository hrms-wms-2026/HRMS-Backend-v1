# Get My Notifications

**GET** `/api/v1/notifications`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** none beyond `TenantPolicy` — returns the caller's own notifications only.

## Description

Lists in-app notifications for the current user (newest first). Query params: `unreadOnly` (default `false`), `page` (default `1`, page size 20).

## Response

`200 OK` — JSON array:

```json
[
  {
    "id": "guid",
    "templateCode": "work_task_creation_request_created",
    "title": "New task request",
    "body": "Priya requested a new task \"Build login\" on Milestone A.",
    "relatedEntityType": "task_creation_request",
    "relatedEntityId": "guid",
    "isRead": false,
    "readAt": null,
    "createdAt": "2026-08-17T00:00:00+00:00"
  }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/SharedPlatform/NotificationsController.cs`
Handler: `src/ONEVO.Application/Features/SharedPlatform/Notifications/Queries/GetMyNotifications/`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-4-notification-foundation.md`
