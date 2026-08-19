# Get Unread Count

**GET** `/api/v1/notifications/unread-count`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.

## Description

Returns the count of unread in-app notifications for the current user (bell badge).

## Response

`200 OK`

```json
{ "count": 3 }
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/SharedPlatform/NotificationsController.cs`
Handler: `src/ONEVO.Application/Features/SharedPlatform/Notifications/Queries/GetUnreadCount/`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-4-notification-foundation.md`
