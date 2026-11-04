# Module attachments (objective assets) — backend implementation (2026-10-28)

Plan: `docs/superpowers/plans/finished/2026-10-28/2026-09-02-objective-asset-attachments-backend.md`
Design: `docs/superpowers/specs/finished/2026-10-28/2026-09-02-module-creation-assets-members-design.md`

## Why now

The frontend (`project-api.service.ts` → `project-detail.store.ts` → `sub-module-form`) has called these endpoints since September, but only the plan and the `objective_asset` upload purpose had been committed. Attaching a file to a module returned 404. Found by the 2026-10-28 docs/code gap audit.

## What was built

| Endpoint | Handler |
|---|---|
| `POST /api/v1/work/objectives/{id}/assets` (multipart `File`) → `201` + attachment | `UploadObjectiveAssetCommandHandler` |
| `DELETE /api/v1/work/objectives/{id}/assets/{assetId}` → `204` | `DeleteObjectiveAssetCommandHandler` |
| `GET /api/v1/work/objectives/{id}/assets/{assetId}/file` → file stream | `GetObjectiveAssetFileQueryHandler` |
| `GET /api/v1/work/objectives/{id}` now includes `assets[]` | `GetObjectiveByIdQueryHandler` |

The response shape matches the frontend's existing `ObjectiveAssetDto` (`id, fileName, sizeBytes, contentType, uploadedAt, downloadUrl`), so no frontend change was needed. Endpoint docs: `docs/postman-request/Work Management/{Upload,Delete,Download} Objective Asset.md`.

## Where it deviates from the September plan, and why

| Plan said | Built | Reason |
|---|---|---|
| Access: module exists in the tenant + `projects:access` | Same rule as reading the module (`*` permission or membership of the module or an ancestor, via `IModuleReadAccess`), shared by all three handlers in `ObjectiveAssetAccess` | The plan's rule let any tenant user with `projects:access` attach to or delete from any module. The codebase moved to membership-based Work Management access after the plan was written. |
| Delete: any caller allowed by the access rule | Only the uploader, the module head, or a `*` admin | Stops one member deleting another member's files. |
| `downloadUrl` = 15-minute signed object-storage URL | API path `/api/v1/work/objectives/{id}/assets/{assetId}/file` | Every download re-checks access (a signed URL keeps working after someone is removed from the module), it works with local file storage, and it matches how task files are served (`/work/tasks/files/{id}`). |
| Never delete the `file_records` row; `IFileStorageService` has no delete | Removes the link, saves, then calls `IFileStorageService.DeleteAsync` | `DeleteAsync` was added after the plan; current remove-attachment code (`DeleteMyDocument`, `TaskAssetLinker`) deletes the file too, so attachments don't leave orphaned files. |
| Plan Task 2: new repository methods | Not needed | `IEntityAssetRepository` already has `ListByOwnerAsync`, `GetByIdForTenantAsync`, `DeleteAsync`. |

## Tests

- Unit: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveAssetHandlersTests.cs` (15 cases: upload by member / non-member / full admin / inactive module / storage rejection; delete by uploader / head / other member / wrong owner / non-member; download by member / non-member / wrong module) and a new case in `GetObjectiveByIdQueryHandlerTests` for the `assets` list. 24/24 in those two classes.
- Integration (real HTTP, Testcontainers PostgreSQL): `TaskAttachmentsIntegrationTests.ModuleAsset_UploadListDownloadDelete_RoundTrip` (upload → listed on detail → downloaded bytes match → another tenant gets 403/404 for download and delete → delete → gone from detail and 404 on download) and `ModuleAsset_DisallowedFileType_Rejected`. 6/6 in that class.
- Full gate: see the commit message for build + unit + architecture results.

## Not covered

- A browser check of the module form's upload list against a running API.
- Uploads against Cloudflare R2: local runs use the configured local storage; R2 behaviour is the same `IFileStorageService` path used by task attachments.
