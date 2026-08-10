# Edit Project

**PUT** `/api/v1/work/projects/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** No `Idempotency-Key` support — a plain last-write-wins update (no optimistic concurrency token; see Global Constraints in the implementation plan for why).

## Description

Updates a Project's editable fields and cascades the same `name`/`description`/`startDate`/`targetDate` onto its Default Objective, in one transaction. `identifier` is immutable — if the request body includes one that differs from the project's current value, the request is rejected with `400`. Only the project's lead may edit it — matches Delete's existing lead-only rule (a Project is the tree's root node; only its own Head has unrestricted control over it, per `docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md` §4).

## Request

Content type: `application/json`.

```json
{
  "name": "Website Revamp v2",
  "description": "Rebuild the marketing site. (optional)",
  "categoryId": "guid — existing, active, tenant-owned project category",
  "startDate": "2026-08-01",
  "targetDate": "2027-01-01",
  "color": "#2563EB (optional, <= 20 chars)",
  "actualHours": "12 (optional, >= 0)",
  "identifier": "WEB (optional — only send if you want the immutability check to run; omit to skip it entirely)"
}
```

## Response

`200 OK`

```json
{
  "id": "guid", "name": "string", "identifier": "string", "categoryId": "guid", "description": "string|null",
  "leadId": "guid", "startDate": "date", "targetDate": "date", "color": "string|null",
  "actualHours": "decimal|null", "allocatedHours": "decimal", "completedHours": "decimal",
  "isActive": true, "createdAt": "datetime", "updatedAt": "datetime|null", "isLead": true
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure (dates, `color` length), or the request tried to change `identifier` |
| `403` | Caller lacks `projects:access`, or has it but is not the project lead |
| `404` | Project doesn't exist in tenant, or `categoryId` invalid/inactive/not tenant-owned |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`Edit`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/EditProject/EditProjectCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md`
