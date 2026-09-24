# Achieve Project

**POST** `/api/v1/work/projects/{id}/achieve`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` + caller must be the Project's Lead (`leadId` compared as the caller's Employee id as of 2026-08-14).
**Idempotent:** No - a second call on an already-achieved project returns `409`.

## Description

Marks a Project Achieved. Every top-level milestone (direct child of the Default Objective) must already be Achieved first. Lead-only, always immediate - the Project is the tree's root, so there's no Reporting Manager to route an approval request to (same root exception as Edit/Delete Project).

## Request

No body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `400` | A top-level milestone isn't yet Achieved |
| `403` | Caller lacks `projects:access`, or is not the project lead |
| `404` | Project doesn't exist in tenant |
| `409` | Project is already achieved |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`Achieve`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AchieveProject/AchieveProjectCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-06-work-management-milestone-membership-and-achieve.md`
