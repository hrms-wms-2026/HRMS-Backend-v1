# Open Conversation

**POST** `/api/v1/conversations/open`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

Gets or creates the group chat of an inbox item and returns it with its participants and newest 50 messages. Base participants (sender and receiver; for alerts the recipients) are resolved from the item at read time, so a transferred approver position moves the chat to the new holder. Only a base participant can create the conversation; added members can open it afterwards.

## Request

`{ "source": "work", "kind": "approval", "id": "…" }`

## Response

`200 OK` — `{ "id": "…", "source": "work", "kind": "approval", "subjectId": "…", "title": "…", "projectId": "…", "participants": [ { "employeeId": "…", "name": "…", "avatarFileId": null, "joinReason": "base", "addedByEmployeeId": null, "addedAt": "…" } ], "messages": [ … ], "hasMoreMessages": false, "lastReadAt": null }`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |
| `404` | Unknown source, missing item, or the caller is not a participant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Conversations/ConversationsController.cs`
Handler: `src/ONEVO.Application/Features/Conversations/Commands/OpenConversation/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
