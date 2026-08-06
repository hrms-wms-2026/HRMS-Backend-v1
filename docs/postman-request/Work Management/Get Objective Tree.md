# Get Objective Tree

**GET** `/api/v1/work/projects/{projectId}/objectives`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must have an active `project_members` row somewhere in this project.

## Description

Every active Objective for a Project, flat (client builds the tree from `parentObjectiveId`). No admin/cross-user visibility permission exists for this endpoint — membership is the only access path (design §6 #8).

## Response

`200 OK` — a JSON array: `[{ "id": "guid", "parentObjectiveId": "guid|null", "isDefault": true, "title": "string", "ownerId": "guid", "startDate": "date", "endDate": "date", "allocatedHours": 40, "completedHours": 0, "isActive": true }]`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller has no active membership in this project |
| `404` | Project doesn't exist in tenant, or is inactive |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetTree`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveTree/GetObjectiveTreeQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-milestone-hierarchy.md`
