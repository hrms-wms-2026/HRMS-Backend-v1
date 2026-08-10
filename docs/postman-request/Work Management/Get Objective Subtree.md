# Get Objective Subtree

**GET** `/api/v1/work/objectives/{id}/tree`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head.

## Description

Returns `{id}`'s parent Objective detail (if any) plus its full nested descendant subtree (children, grandchildren, ...), each carrying the full detail field set. Independent of `GET /api/v1/work/projects/{projectId}/objectives` — this is a Head-only, single-milestone read, not a project-wide one. Inactive (soft-deleted) descendants are included; the client filters on `isActive` if it only wants live nodes.

## Response

`200 OK`:

```json
{
  "parentObjective": { "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true, "createdAt": "datetime", "updatedAt": "datetime|null" } | null,
  "objective": {
    "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null",
    "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date",
    "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true,
    "createdAt": "datetime", "updatedAt": "datetime|null",
    "children": []
  }
}
```

`parentObjective` is `null` when `{id}` has no parent (i.e., it's the Project's Default Objective). Each entry in `children` has the same shape as `objective`, recursively.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller is not `{id}`'s current Head, or lacks `projects:access` |
| `404` | Objective doesn't exist in tenant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetSubtree`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-07-work-management-objective-subtree.md`
