# Get Inbox Item Detail

**GET** `/api/v1/inbox/{source}/{kind}/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

The item plus its request steps timeline, summary, change set (`field`, `label`, `before`, `after`), alert explanation, target link and, for approvers of editable requests, the editable payload. Kinds for `work`: `approval`, `invitation`, `monitor_alert`.

## Response

`200 OK` — `{ "item": { … }, "steps": [ { "key": "requested", "label": "Requested", "actor": { … }, "at": "…", "state": "done", "note": null } ], "summary": "…", "changeSet": [ … ], "explanation": null, "explanationDetails": null, "target": { "route": ["/work", "…"], "label": "Open task" }, "editablePayloadJson": null }`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |
| `404` | Unknown source, or the item does not exist / is not visible to the caller |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Inbox/InboxController.cs`
Handler: `src/ONEVO.Application/Features/Inbox/Queries/GetInboxItemDetail/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
