# Post Conversation Message

**POST** `/api/v1/conversations/{id}/messages`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

Posts a chat message. Mentions must be current participants (use Add Conversation Participants first for "add and tag"); each mentioned participant gets a `conversation_mentioned` bell notification. Active content (scripts, event handlers, script URLs) is rejected. Participants receive `messageCreated` live.

## Request

`{ "bodyHtml": "<p>Hi <span class=\"mention\" data-employee-id=\"…\">@Anu</span></p>", "mentionEmployeeIds": ["…"] }`

## Response

`200 OK` — the created message (same shape as List Conversation Messages items).

## Errors

| Status | Cause |
|---|---|
| `400` | Empty, too long (10 000 chars) or contains active content |
| `403` | Not authenticated, or the user has no employee record |
| `404` | Conversation not found or caller is not a participant |
| `422` | A mentioned employee is not a participant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Conversations/ConversationsController.cs`
Handler: `src/ONEVO.Application/Features/Conversations/Commands/PostConversationMessage/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
