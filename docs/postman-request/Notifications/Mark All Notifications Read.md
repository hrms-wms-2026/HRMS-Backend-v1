# Mark All Notifications Read

**POST** `/api/v1/notifications/read-all`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.

## Description

Marks every unread notification for the current user as read.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/SharedPlatform/NotificationsController.cs`
Handler: `src/ONEVO.Application/Features/SharedPlatform/Notifications/Commands/MarkAllNotificationsRead/`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-4-notification-foundation.md`
