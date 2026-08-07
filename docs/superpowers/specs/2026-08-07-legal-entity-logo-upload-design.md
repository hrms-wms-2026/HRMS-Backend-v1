# Legal Entity Logo Upload — Design

## Problem

The General Settings page's "Upload logo" button is hard-disabled with a "Coming soon" badge
(`general-settings.component.html:38-41`), even though most of the backend logo plumbing already
exists: `SetLegalEntityLogoCommandHandler`, `RemoveLegalEntityLogoCommandHandler`,
`LegalEntityLogoResponse`, and a working `DELETE /{id}/logo` endpoint.

The `PUT /{id}/logo` endpoint was deliberately never exposed
(`LegalEntitiesController.cs:15-18`): `SetLegalEntityLogoCommandHandler` sets `LogoFileId` from a
client-supplied GUID with no way to verify that file belongs to the tenant, because
`IFileStorageService` — the only interface any feature outside Storage.File may call — exposes no
"look up an existing file" method, only upload/reservation flows.

A second, previously unflagged gap: even the working `DELETE` path has no way to *display* a logo.
`LegalEntityGeneralSettingsResponse` only exposes `LogoFileId` (a raw GUID); there is no
public-URL field or file-serving endpoint anywhere in the codebase.

A third gap, found while designing the display path: `IStorageService`
(`Common/ServiceInterfaces/IStorageService.cs`), which exposes a `GetPublicUrl(filePath)` method,
has **zero implementations and zero consumers** anywhere in the codebase — dead code, not wired to
R2. The real, live pipeline (`IObjectStorageAdapter` → `CloudflareR2ObjectStorageAdapter`) only
supports `PutObject`/`GetObject(Stream)`/`Delete`/`Exists` against a private bucket with
credentials resolved at runtime via `IPlatformServiceKeyResolver` — no public URL, no presigned
URL, no CDN domain exists anywhere. There is no way to hand the browser a direct link to a stored
file today.

## Precedent

`UploadFaceScanCommandHandler` (`Features/Monitoring/CheckIn/Commands/UploadFaceScan/`) already
solves the same shape of problem: it accepts an upload directly in its own handler, calls
`IFileStorageService.UploadAsync(tenantId, userId, fileName, contentType, purpose, stream)`, and
links the resulting `FileRecordDto.Id` to its domain entity — all in one request, all tenant-scoped
by construction. No lookup of a pre-existing file ever happens.

`UploadPurposeCatalog.CompanyLogo` already exists (5 MB, PNG/JPEG/WebP) and is currently unused.

## Decision

Follow the `UploadFaceScanCommandHandler` pattern: make `PUT /{id}/logo` a direct multipart upload,
not a two-step "upload elsewhere, then reference by FileId" flow. This sidesteps the *upload-side*
blocker entirely — nothing ever needs to validate an arbitrary client-supplied `FileId`'s
ownership, so this half requires no change to `IFileStorageService` or its architecture tests.

The *display* side does need one small, deliberate addition to `IFileStorageService`, because no
public/presigned URL mechanism exists anywhere in the codebase (see Problem, gap 3): a read-only
`OpenReadAsync(tenantId, storageKey, ct)` that streams bytes for a storage key the caller already
legitimately owns. This is a fundamentally different, smaller trust decision than the one Part 2C
deferred — that was "validate an *untrusted, client-supplied* file id belongs to this tenant
before acting on it"; this is "stream back bytes for a key this feature generated and stored
itself." No ownership-validation logic is needed because there is no untrusted input to validate.

The now-dead `IStorageService` interface (gap 3) is deleted as part of this change — it has no
implementation, no consumers, and no DI registration, and leaving it in place after specifically
finding it here would be misleading dead code in the area this feature touches.

Store the resulting `StorageKey` on `LegalEntity` at upload time (the handler already has it in
hand from `UploadAsync`'s result), so the display endpoint never needs a `FileId → StorageKey`
lookup either — the entity carries what it needs directly.

Storage quota accounting needs no new work: `IFileStorageService.UploadAsync` — which the new
handler calls — already reserves bytes against `IStorageQuotaService`/`tenant_storage_stats`
before writing anything (`FileStorageService.cs:67`, inside `BeginReservationAsync`). Logo uploads
are quota-tracked automatically by reusing this pipeline. Likewise, Cloudflare R2 credentials are
already resolved via the existing encrypted `platform_service_keys` system
(`IPlatformServiceKeyResolver` / `PlatformServiceKeyCatalog.CloudflareR2`) — nothing new needed
there.

## Backend design

**Data model:** add nullable `LogoStorageKey` (string) to `LegalEntity`, alongside the existing
`LogoFileId`. New migration.

**`IFileStorageService` addition** (`Features/Storage/File/ServiceInterfaces/IFileStorageService.cs`):
- New method: `Task<Result<FileStreamDto>> OpenReadAsync(Guid tenantId, string storageKey,
  CancellationToken ct = default)`, where `FileStreamDto` is a new small record
  `(Stream Content, string ContentType)`.
- Implemented in `FileStorageService` by delegating to `_objectStorage.GetObjectAsync(storageKey,
  ct)`, wrapped in the same `try/catch (ObjectStorageException) → 502` pattern `UploadAsync`
  already uses. Tenant-scoped: the parameter exists so callers are forced to think about tenant
  context, even though the storage key itself is already tenant-namespaced by
  `IUploadPurposePolicy.GenerateStorageKey`.
- This is the one interface addition this feature makes to Storage.File — deliberately minimal
  and read-only (see Decision).

**Remove dead code:** delete `IStorageService.cs`
(`Common/ServiceInterfaces/IStorageService.cs`) — zero implementations, zero consumers, not wired
to anything (see Problem, gap 3).

**Upload** — `PUT /api/v1/org/legal-entities/{id}/logo`, `legal_entity:update`, currently
unexposed:
- Controller accepts `multipart/form-data` (`IFormFile`), not a `FileId`.
- `SetLegalEntityLogoCommand` changes from `(LegalEntityId, FileId)` to
  `(LegalEntityId, Stream, ContentType, FileName)`.
- Handler calls `IFileStorageService.UploadAsync(tenantId, userId, fileName, contentType,
  UploadPurposeCatalog.CompanyLogo, stream, ct)`. `UploadAsync` enforces the 5 MB / PNG-JPEG-WebP
  rule server-side, and reserves tenant storage quota as a side effect (see Decision).
- On success: sets `entity.LogoFileId` and `entity.LogoStorageKey` from the returned
  `FileRecordDto`, saves.
- Returns `LegalEntityLogoResponse`, extended with a new `LogoUrl` field: the *route* to the new
  display endpoint below (`/api/v1/org/legal-entities/{id}/logo`), not a storage URL — doubles as
  a simple "has a logo" signal for the frontend.
- Remove the now-stale "PUT /{id}/logo is deliberately not exposed" comment block
  (`LegalEntitiesController.cs:15-18`) and the matching comment in
  `SetLegalEntityLogoCommandHandler.cs:9-16`.

**Display** — new `GET /api/v1/org/legal-entities/{id}/logo`, `legal_entity:update`:
- Loads the entity for the current tenant, 404s if missing or if `LogoStorageKey` is null.
- Calls `IFileStorageService.OpenReadAsync(tenantId, entity.LogoStorageKey, ct)` and returns
  `File(stream, contentType)`.
- Same route as the `LogoUrl` field returned by the upload/settings responses. Browser `<img>`
  requests to it carry the existing `onevo_session` cookie automatically (HttpOnly,
  `SameSite=Strict`, no `Domain` restriction — same-site subresource requests are unaffected by
  `SameSite=Strict`), so no token/header plumbing is needed on the frontend.

**Remove** — `DELETE /{id}/logo`, already working: also clears `LogoStorageKey`.

**Settings display** — `LegalEntityGeneralSettingsResponse` (the GET that populates the whole
settings page) gains the same `LogoUrl` field (route string, `null` when no logo is set), so the
page shows the current logo on load, not only right after an upload. `LegalEntityMapper` updated
accordingly.

**Existing test guards to flip, not delete** (both currently assert this feature doesn't exist):
- `LegalEntitiesControllerArchitectureTests.NoPutLogoRoute_Exists` → replace with an assertion
  that `PUT /logo` exists and uses `legal_entity:update`. Update the class doc comment
  (lines 11-16) which currently states the deferral as intentional.
- `LegalEntitiesIntegrationTests.SetLogo_RouteDoesNotExist_ByDesign` → replace with a real
  happy-path multipart upload test.

## Frontend design

**Models & API service** (`legal-entity.model.ts`, `legal-entity-api.service.ts`):
- `LegalEntityGeneralSettings` gains `logoUrl: string | null`.
- `LegalEntityApiService` gains `uploadLogo(legalEntityId, file: File)` (builds `FormData`, `PUT`s
  multipart) and `removeLogo(legalEntityId)` (`DELETE`). Both return `{ logoFileId, logoUrl }`.

**Store** (`general-settings.store.ts`):
- New `uploadingLogo` / `removingLogo` flags.
- `uploadLogo()` / `removeLogo()` call the service, then patch just `logoFileId`/`logoUrl` onto the
  existing `settings` object, guarded by the same `legalEntityId()` race check `load()`/`save()`
  already use.
- Errors go through the existing `extractErrorMessage` helper.

**Component** (`general-settings.component.ts` / `.html`):
- Replace the disabled button with one wired to a hidden
  `<input type="file" accept="image/png,image/jpeg,image/webp">`.
- Client-side pre-check (type + 5 MB) before calling `uploadLogo()`, surfaced via
  `notificationService.error(...)` on rejection. Server remains authoritative regardless.
- When `settingsStore.settings()?.logoUrl` is set, render an `<img>` preview in place of the
  placeholder icon, with a **Remove** button next to it calling `removeLogo()`.
- Update hint text from "Max 2 MB" to "Max 5 MB".
- Disable both buttons while `uploadingLogo()` / `removingLogo()` is true.

## Testing

**Backend:**
- Extend `LegalEntityLogoCommandHandlerTests`: upload success/failure via mocked
  `IFileStorageService`; remove clears both `LogoFileId` and `LogoStorageKey`.
- Unit test for `FileStorageService.OpenReadAsync`: success delegates to
  `IObjectStorageAdapter.GetObjectAsync`; `ObjectStorageException` maps to a 502 `Result`.
- Extend `LegalEntitiesIntegrationTests`: multipart `PUT /logo` happy path, oversized/wrong-type
  rejection (via the existing `UploadPurposeCatalog.CompanyLogo` rule), `GET /logo` returns the
  bytes/content-type, `GET /logo` 404s with no logo set, `DELETE` clears `LogoUrl`.
- Flip `LegalEntitiesControllerArchitectureTests.NoPutLogoRoute_Exists` and
  `LegalEntitiesIntegrationTests.SetLogo_RouteDoesNotExist_ByDesign` (see Backend design).

**Frontend:**
- Service spec: `FormData`/URL construction for both calls.
- Store spec: upload/remove success and error paths, race-guard behavior.
- Component spec: file-select → upload flow, preview rendering, remove-button flow, client-side
  type/size rejection.

## Out of scope

- Presigned direct-to-R2 client upload or a public/CDN bucket domain (unnecessary infrastructure
  work for a ≤5 MB proxied image).
- Any change to `IFileStorageService`'s upload/reservation methods or the ownership-validation
  lookup Part 2C deferred — only the new read-only `OpenReadAsync` is added.
- Logo display anywhere outside the General Settings page (e.g., sidebar branding) — not
  requested.
