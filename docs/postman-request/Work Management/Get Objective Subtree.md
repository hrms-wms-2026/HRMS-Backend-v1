# Get Objective Subtree

**GET** `/api/v1/work/objectives/{id}/tree`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + (`projects:read`/`*` OR an active membership on this milestone or any of its ancestors — checked in-handler, not via `[RequirePermission]`, same pattern as Get Objective).

## Description

Returns `{id}`'s parent Objective detail (if any) plus its full nested descendant subtree (children, grandchildren, ...), each carrying the full detail field set. Independent of `GET /api/v1/work/projects/{projectId}/objectives` — this is a single-milestone read, not a project-wide one. No longer Head-only as of 2026-08-10 — any project member with access to this milestone (or an ancestor of it) can read its subtree; only Edit/Achieve/Unachieve stay Head-restricted. Inactive (soft-deleted) descendants are included; the client filters on `isActive` if it only wants live nodes.

## Response

`200 OK`:

```json
{
  "parentObjective": { "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true, "isAchieved": false, "achievedAt": "datetime|null", "createdAt": "datetime", "updatedAt": "datetime|null", "ownerName": "string|null", "reportingManagerName": "string|null", "isOwner": false } | null,
  "objective": {
    "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null",
    "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date",
    "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true,
    "createdAt": "datetime", "updatedAt": "datetime|null",
    "ownerName": "string|null", "reportingManagerName": "string|null", "isOwner": false, "isAchieved": false, "achievedAt": "datetime|null",
    "children": []
  }
}
```

`parentObjective` is `null` when `{id}` has no parent (i.e., it's the Project's Default Objective). Each entry in `children` has the same shape as `objective`, recursively. Added 2026-08-10: `ownerName`/`reportingManagerName` (resolved once across every node in the project, `null` if not found), `isOwner` (per-node, true only when the caller is that specific node's owner — not inherited from an ancestor), and `isAchieved`/`achievedAt` (previously only returned by the single Get Objective endpoint, now also on every subtree node) — added for the Project Detail milestone tree view.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or has neither `projects:read`/`*` nor an active membership on this milestone or an ancestor of it |
| `404` | Objective doesn't exist in tenant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetSubtree`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-07-work-management-objective-subtree.md`
