# List Conversation Messages

**GET** `/api/v1/conversations/{id}/messages?before=&take=`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

Older messages for "load older": messages created before `before` (ISO date-time; newest page when omitted), oldest first. `take` defaults to 50 (max 100).

## Response

`200 OK` — `{ "items": [ { "id": "…", "conversationId": "…", "authorEmployeeId": "…", "authorName": "…", "authorAvatarFileId": null, "kind": "text", "bodyHtml": "<p>…</p>", "systemEventJson": null, "mentionEmployeeIds": [], "createdAt": "…" } ], "hasMore": true }`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |
| `404` | Conversation not found or caller is not a participant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Conversations/ConversationsController.cs`
Handler: `src/ONEVO.Application/Features/Conversations/Queries/ListConversationMessages/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
