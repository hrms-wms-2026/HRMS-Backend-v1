# Create Project

**POST** `/api/v1/work/projects`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** Yes — send an `Idempotency-Key` header; retrying the same key returns the original result.

## Description

Creates a Project in one atomic transaction. A single call always produces: the Project itself, one Default Objective, one creator membership (linking the caller to that Default Objective), one Default Version (status `planned`), one release-calendar reminder, zero or more Labels, and — if a logo file is attached — one `entity_assets` row linking the uploaded file as the project's cover image.

## Request

Content type: `multipart/form-data` (not raw JSON — a file field is included). Shown below as JSON for readability; each key is one form field.

```json
{
  "categoryId": "guid — existing, active, tenant-owned project category",
  "name": "Website Revamp",
  "identifier": "WEB — letters/digits only, must start with a letter, max 20 chars, unique per tenant",
  "description": "Rebuild the marketing site. (optional)",
  "startDate": "2026-08-01",
  "targetDate": "2026-12-01",
  "releaseDate": "2026-11-15",
  "color": "#2563EB (optional)",
  "actualHours": "0 (optional, >= 0)",
  "defaultObjectiveAllocatedHours": "40 (>= 0)",
  "labelsJson": "[{\"name\":\"Backend\",\"color\":\"#111111\"}] — JSON-encoded array, sent as a single text field; names must be unique in the request",
  "logo": "(optional file field) image only, <= 5 MB"
}
```

## Response

`201 Created`, `Location: /api/v1/work/projects/{projectId}`

```json
{
  "project": { "id": "guid", "name": "string", "identifier": "string", "categoryId": "guid", "description": "string|null", "leadId": "guid", "startDate": "date", "targetDate": "date", "color": "string|null", "actualHours": "decimal|null", "allocatedHours": "decimal", "completedHours": "decimal", "isActive": true, "createdAt": "datetime" },
  "defaultObjective": { "id": "guid", "projectId": "guid", "isDefault": true, "title": "string", "ownerId": "guid", "startDate": "date", "endDate": "date", "allocatedHours": "decimal", "completedHours": "decimal" },
  "defaultVersion": { "id": "guid", "name": "string", "statusId": 1, "statusCode": "planned" },
  "releaseReminder": { "id": "guid", "versionId": "guid", "scheduledDate": "date", "reminderType": "project_release" },
  "labels": [ { "id": "guid", "name": "string", "color": "string" } ],
  "creatorMembership": { "id": "guid", "objectiveId": "guid", "userId": "guid", "membershipSource": "system" },
  "logo": { "fileRecordId": "guid", "originalFileName": "string" } 
}
```

`logo` is `null` in the response when no file was uploaded.

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure (see field rules above) |
| `403` | Not authenticated, no tenant context, or no employee record for the current user |
| `404` | `categoryId` does not exist / not active / not tenant-owned |
| `409` | `identifier` already used by another project in this tenant, or duplicate label names in the request |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs`
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandHandler.cs`
Response mapping: `src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectCreationViewModel.cs` + `ProjectViewModelMapper.cs` — the controller maps the handler's `ProjectCreationResponse` (Application-layer DTO) to `ProjectCreationViewModel` (API-layer view model) before returning it; field names/shape are a 1:1 mirror, so the response example above is unaffected.
Plan: `docs/superpowers/plans/2026-08-03-work-management-foundation.md`, `docs/superpowers/plans/2026-08-03-view-model-retrofit-phase1.md` (added the ViewModel mapping layer)
