# Delete Objective Asset

**DELETE** `/api/v1/work/objectives/{id}/assets/{assetId}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Access:** the caller must be able to read the module (see Upload Objective Asset) **and** be the person who attached the file, the module's head, or hold a `*` permission.

## Description

Removes the attachment link (`entity_assets` row) and then deletes the stored file.

## Request

No body. `{assetId}` is the `id` returned by Upload Objective Asset or listed in Get Objective's `assets`.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Not a member of this module or an ancestor, or a member who neither attached the file nor heads the module |
| `404` | Module doesn't exist / is inactive, or the attachment doesn't belong to this module |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`DeleteAsset`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjectiveAsset/DeleteObjectiveAssetCommandHandler.cs`
