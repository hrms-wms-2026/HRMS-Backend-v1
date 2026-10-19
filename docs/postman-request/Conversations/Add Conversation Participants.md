# Add Conversation Participants

**POST** `/api/v1/conversations/{id}/participants`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

Any participant can add active employees of the tenant. Each new member joins at once, gets a `conversation_member_added` bell notification, and a system message "X added Y" is posted. Added members can view and chat but never approve, reject, cancel or revert. Existing members are skipped.

## Request

`{ "employeeIds": ["…"] }`

## Response

`200 OK` — the newly added participants (empty when everyone was already a member).

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |
| `404` | Conversation not found or caller is not a participant |
| `422` | An employee is inactive or not in this organization |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Conversations/ConversationsController.cs`
Handler: `src/ONEVO.Application/Features/Conversations/Commands/AddConversationParticipants/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
