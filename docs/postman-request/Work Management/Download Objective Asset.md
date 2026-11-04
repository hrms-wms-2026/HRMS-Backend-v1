# Download Objective Asset

**GET** `/api/v1/work/objectives/{id}/assets/{assetId}/file`

**Auth:** Tenant session cookie (`onevo_session`). Policy: `TenantPolicy`.
**Access:** same rule as Get Objective: a `*` permission, or an active member of this module or any ancestor module.

## Description

Streams the attached file with its original file name (`Content-Disposition: attachment`). This is the `downloadUrl` returned for every attachment.

## Response

`200 OK` with the file body and its stored content type.

## Errors

| Status | Cause |
|---|---|
| `403` | Not a member of this module or an ancestor |
| `404` | Module doesn't exist / is inactive, the attachment doesn't belong to this module, or the file was deleted |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetAssetFile`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveAssetFile/GetObjectiveAssetFileQueryHandler.cs`
