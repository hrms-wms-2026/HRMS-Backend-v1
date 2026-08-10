# Delete Project

**DELETE** `/api/v1/work/projects/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` **and** caller must be the project's `leadId`.
**Idempotent:** No — a second call against an already-deleted project returns `409`, not a silent `204`.

## Description

Soft-deletes a Project (`is_active = false`, `updated_at` bumped). No cascade — `objectives`/`project_members`/`release_calendar`/etc. rows are untouched and keep their own independent lifecycle.

## Request

No body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or has it but is not the project lead |
| `404` | Project doesn't exist in tenant |
| `409` | Project is already soft-deleted |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`Delete`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/DeleteProject/DeleteProjectCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md`
