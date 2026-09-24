# Mark Notification Read

**POST** `/api/v1/notifications/{id}/read`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.

## Description

Marks a single notification as read. Caller must be the recipient. Idempotent if already read.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated |
| `404` | Notification not found for this user/tenant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/SharedPlatform/NotificationsController.cs`
Handler: `src/ONEVO.Application/Features/SharedPlatform/Notifications/Commands/MarkNotificationRead/`
Plan: `docs/superpowers/plans/next/2026-08-16-work-management-task-foundation/part-4-notification-foundation.md`
