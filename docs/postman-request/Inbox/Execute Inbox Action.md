# Execute Inbox Action

**POST** `/api/v1/inbox/{source}/{kind}/{id}/actions/{action}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

Runs one of the item's `availableActions` through the source module's existing commands and rules (WM: `approve`, `reject`, `cancel`, `revert`, `accept_invitation`, `decline_invitation`). People added to the conversation can never decide. After success, the affected people get a live `inboxChanged` event.

## Request

Body (optional, action-specific): `{ "comment": "…", "editedPayloadJson": "…" }` — `reject` requires a comment.

## Response

`200 OK` — `{ "affectedEmployeeIds": ["…"], "message": null }`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |
| `403/422` | The module's own rule refused the action (e.g. not the approver) |
| `404` | Unknown source/kind/action or item not visible |
| `409` | Already decided |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Inbox/InboxController.cs`
Handler: `src/ONEVO.Application/Features/Inbox/Commands/ExecuteInboxAction/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
