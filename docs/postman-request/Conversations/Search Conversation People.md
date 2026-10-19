# Search Conversation People

**GET** `/api/v1/conversations/people?search=`

**Auth:** Tenant session cookie (`onevo_session`). Policy: `TenantPolicy`.

## Description

Active employees of the tenant to add to a conversation (or add and tag), matched by name (case-insensitive; all when `search` is empty), ordered by name, at most 20. The caller is left out. Not limited by the caller's People permissions or reporting line; only name and avatar are returned.

## Response

`200 OK` — `[ { "employeeId": "…", "name": "Priya Raj", "avatarFileId": null } ]`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Conversations/ConversationsController.cs`
Handler: `src/ONEVO.Application/Features/Conversations/Queries/SearchConversationPeople/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
