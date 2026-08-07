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

## Precedent

`UploadFaceScanCommandHandler` (`Features/Monitoring/CheckIn/Commands/UploadFaceScan/`) already
solves the same shape of problem: it accepts an upload directly in its own handler, calls
`IFileStorageService.UploadAsync(tenantId, userId, fileName, contentType, purpose, stream)`, and
links the resulting `FileRecordDto.Id` to its domain entity — all in one request, all tenant-scoped
by construction. No lookup of a pre-existing file ever happens.

`UploadPurposeCatalog.CompanyLogo` already exists (5 MB, PNG/JPEG/WebP) and is currently unused.

## Decision

Follow the `UploadFaceScanCommandHandler` pattern: make `PUT /{id}/logo` a direct multipart upload,
not a two-step "upload elsewhere, then reference by FileId" flow. This sidesteps the original
blocker entirely — nothing ever needs to validate an arbitrary client-supplied `FileId`'s
ownership, so `IFileStorageService` and its architecture tests are untouched.

To solve the display gap without adding a lookup method either, store the resulting
`StorageKey` on `LegalEntity` at upload time (the handler already has it in hand from
`UploadAsync`'s result), and compute a public URL from it locally wherever the logo needs to be
shown.

## Backend design

**Data model:** add nullable `LogoStorageKey` (string) to `LegalEntity`, alongside the existing
`LogoFileId`. New migration.

**Upload** — `PUT /api/v1/org/legal-entities/{id}/logo`, `legal_entity:update`, currently
unexposed:
- Controller accepts `multipart/form-data` (`IFormFile`), not a `FileId`.
- `SetLegalEntityLogoCommand` changes from `(LegalEntityId, FileId)` to
  `(LegalEntityId, Stream, ContentType, FileName)`.
- Handler calls `IFileStorageService.UploadAsync(tenantId, userId, fileName, contentType,
  UploadPurposeCatalog.CompanyLogo, stream, ct)`. `UploadAsync` enforces the 5 MB / PNG-JPEG-WebP
  rule server-side.
- On success: sets `entity.LogoFileId` and `entity.LogoStorageKey` from the returned
  `FileRecordDto`, saves.
- Returns `LegalEntityLogoResponse`, extended with a new `LogoUrl` field:
  `IStorageService.GetPublicUrl(entity.LogoStorageKey)`.
- Remove the now-stale "PUT /{id}/logo is deliberately not exposed" comment block
  (`LegalEntitiesController.cs:15-18`) and the matching comment in
  `SetLegalEntityLogoCommandHandler.cs:9-16`.

**Remove** — `DELETE /{id}/logo`, already working: also clears `LogoStorageKey`, so `LogoUrl` goes
back to `null` in the response.

**Display** — `LegalEntityGeneralSettingsResponse` (the GET that populates the whole settings
page) gains the same `LogoUrl` field, computed the same way, so the page shows the current logo on
load, not only right after an upload. `LegalEntityMapper` updated accordingly.

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
- Extend `LegalEntitiesIntegrationTests`: multipart `PUT /logo` happy path, oversized/wrong-type
  rejection (via the existing `UploadPurposeCatalog.CompanyLogo` rule), `DELETE` clears `LogoUrl`.
- Check `LegalEntitiesControllerArchitectureTests` / `LegalEntityGeneralSettingsArchitectureTests`
  don't assert the `PUT /logo` route is absent; update if so.

**Frontend:**
- Service spec: `FormData`/URL construction for both calls.
- Store spec: upload/remove success and error paths, race-guard behavior.
- Component spec: file-select → upload flow, preview rendering, remove-button flow, client-side
  type/size rejection.

## Out of scope

- Presigned direct-to-R2 client upload (unnecessary complexity for a ≤5 MB image).
- Any change to `IFileStorageService`'s public interface or its architecture tests.
- Logo display anywhere outside the General Settings page (e.g., sidebar branding) — not
  requested.
