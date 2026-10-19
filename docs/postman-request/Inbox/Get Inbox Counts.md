# Get Inbox Counts

**GET** `/api/v1/inbox/counts`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

Rail badge numbers: pending requests addressed to the caller, unread chat messages across the caller's items, and open alerts (alerts on achieved modules are not counted).

## Response

`200 OK` — `{ "pendingReceived": 3, "unreadMessages": 5, "openAlerts": 1 }`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Inbox/InboxController.cs`
Handler: `src/ONEVO.Application/Features/Inbox/Queries/GetInboxCounts/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
