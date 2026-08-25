# Get Project Banner

**GET** `/api/v1/work/projects/{id}/banner`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:read` **OR** `*` **OR** an active `project_members` row for this project — checked by the handler, not by `[RequirePermission]` (which would hard-block members lacking the tenant-wide permission). Same access rule as `GET /api/v1/work/projects/{id}/logo`.
**Idempotent:** GET (safe/idempotent by HTTP semantics). No `Idempotency-Key` header.

## Description

Streams the project's banner image (`entity_assets.asset_purpose = project_banner`). This is a separate optional upload from the logo (`project_cover`); a project may have logo only, banner only, both, or neither. Returns `404` when no banner asset exists.

## Request

No body. `{id}` is the project's Guid.

## Response

`200 OK` with the stored file bytes.

Content type: the banner's stored content type (one of `image/png`, `image/jpeg`, `image/webp`).

Body: raw image bytes (not JSON).

## Errors

| Status | Cause |
|---|---|
| `403` | Not authenticated, no tenant context, no employee record, or caller has neither `projects:read`/`*` nor an active membership row for this project |
| `404` | Project doesn't exist in tenant, or no `project_banner` entity asset is set |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`GetBanner`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectBanner/GetProjectBannerQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-20-work-management-project-page-redesign/part-1-release-date-and-banner-image.md`
