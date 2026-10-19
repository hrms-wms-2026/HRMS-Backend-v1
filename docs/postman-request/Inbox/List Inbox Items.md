# List Inbox Items

**GET** `/api/v1/inbox`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

Every request and alert that involves the caller, across all projects and source modules (Work Management today). Pending first, then most recently updated. Common filters apply to every source; source-specific filters use `facet=key:value`.

## Request

Query (repeat a key for several values): `category` (request|alert), `source` (work), `project` (guid), `status`, `direction` (received|sent|info), `type` (action type / alert rule), `requestedBy` (employee guid), `from`, `to` (ISO date-time, on createdAt), `search`, `facet` (e.g. `module:{moduleId}`), `page` (default 1), `pageSize` (default 25, max 100).

## Response

`200 OK`

```json
{ "items": [ { "source": "work", "kind": "approval", "id": "…", "category": "request", "actionType": "task_edit", "title": "…", "subtitle": "…", "status": "pending", "direction": "received", "severity": null, "contextType": "project", "contextId": "…", "contextName": "Payments", "requestedBy": { "id": "…", "name": "Bala" }, "requestedTo": { "id": "…", "name": "Anu" }, "createdAt": "…", "updatedAt": "…", "availableActions": [ { "key": "approve", "label": "Approve", "tone": "primary", "needsComment": false, "needsPayload": false } ], "unreadMessages": 2, "isPending": true, "facets": { "module": ["…"] } } ], "total": 1, "page": 1, "pageSize": 25 }
```

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Inbox/InboxController.cs`
Handler: `src/ONEVO.Application/Features/Inbox/Queries/ListInboxItems/ (router: Features/Inbox/Services/InboxRouter.cs)`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
