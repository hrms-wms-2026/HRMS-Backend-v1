# Get Inbox Filters

**GET** `/api/v1/inbox/filters`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header on POST. Policy: `TenantPolicy`.

## Description

The filter fields the Inbox page renders: a `source` field from the router plus each provider's fields (Project, Type, Status, Direction, Requested by, Module). Fields with the same key are merged across providers.

## Response

`200 OK` — `[ { "key": "project", "label": "Project", "multi": true, "options": [ { "value": "…", "label": "Payments" } ] } ]`

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, or the user has no employee record |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/Inbox/InboxController.cs`
Handler: `src/ONEVO.Application/Features/Inbox/Queries/GetInboxFilters/`
Plan: `docs/superpowers/plans/next/2026-10-13-unified-inbox.md`
