# Mark Conversation Read

**POST** `/api/v1/conversations/{id}/read`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

Sets the caller's read marker to now; the item's unread count drops to zero.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |
| `404` | Conversation not found or caller is not a participant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Conversations/ConversationsController.cs`
Handler: `src/ONEVO.Application/Features/Conversations/Commands/MarkConversationRead/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
