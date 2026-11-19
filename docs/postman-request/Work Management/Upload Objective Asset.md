# Upload Objective Asset

**POST** `/api/v1/work/objectives/{id}/assets`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Access:** checked in-handler, same rule as Get Objective: a `*` permission, or an active member of this module or any ancestor module.

## Description

Uploads one file and attaches it to the module (sub-module). The file is stored under the `objective_asset` upload purpose and linked through `entity_assets` (`owner_type = objective`). One file per call; the module form uploads files one after another.

Allowed types: `pdf, doc, docx, xls, xlsx, png, jpg, jpeg, gif, zip`. Maximum size: 25 MB. The file's content signature is checked against its extension.

## Request

`multipart/form-data` with one field:

| Field | Type | Notes |
|---|---|---|
| `File` | file | Required |

## Response

`201 Created`

```json
{
  "id": "c0a8012e-...",
  "fileName": "brief.pdf",
  "sizeBytes": 20480,
  "contentType": "application/pdf",
  "uploadedAt": "2026-10-28T09:00:00+00:00",
  "downloadUrl": "/api/v1/work/objectives/{id}/assets/c0a8012e-.../file"
}
```

`id` is the attachment id used by Delete Objective Asset and the download URL. `downloadUrl` is an API path on this host, so every download re-checks access.

## Errors

| Status | Cause |
|---|---|
| `400` | No file, unsupported type, or content that does not match the extension |
| `403` | No employee record, or the caller is not a member of this module or an ancestor |
| `404` | Module doesn't exist or is inactive |
| `413` | File larger than 25 MB |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`UploadAsset`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UploadObjectiveAsset/UploadObjectiveAssetCommandHandler.cs`
Access: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/ObjectiveAssetAccess.cs`
Plan: `docs/superpowers/plans/finished/2026-10-28/2026-09-02-objective-asset-attachments-backend.md`
