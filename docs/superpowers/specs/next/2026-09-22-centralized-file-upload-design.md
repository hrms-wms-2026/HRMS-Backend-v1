# Centralized File/Photo Upload System Design

**Status:** Approved (chat brainstorm, 2026-09-22)

## Context

The user's Profile modal ("Personal Information" tab) has a Profile photo section with a
"Change photo" button; selecting a new photo appears to silently do nothing — the avatar never
visibly updates. Separately, the user wants every photo/file upload in the product to go through
one centralized endpoint, mirroring how the backend already centralizes file *storage* around a
shared table.

## Current State (verified against code, not assumed)

### Backend already has two centralizing tables, only one of which is used broadly

- `file_records` (migration `20260719081138_AddFileStorageTables.cs`) — the actual uploaded-file
  record: `storage_key`, `content_type`, `file_size_bytes`, `checksum_sha256`, tenant-scoped, RLS
  enforced. Storage provider is Cloudflare R2 (S3-compatible) via `IObjectStorageAdapter` →
  `CloudflareR2ObjectStorageAdapter`. The single mandated write path is `IFileStorageService`
  (`Features/Storage/File/ServiceInterfaces/IFileStorageService.cs`) — its own doc comment states no
  handler may write `file_records`/reserve quota/call the storage adapter directly.
- `entity_assets` — a generic file↔entity link table: `owner_type`, `owner_id`, `asset_purpose`,
  `file_record_id` (FK → `file_records`), `is_primary`, `sort_order`. **Only wired up for
  `EntityAssetOwnerTypes.Project`, `.Objective`, `.Task`** (`Common/Constants/EntityAssetOwnerTypes.cs`)
  — not employees, not legal entities. So today's "centralization" only covers Work Management.
- `UploadPurposeCatalog` (`Features/Storage/File/Helpers/UploadPurposeCatalog.cs`) already declares
  size/type rules per purpose: `company_logo`, `employee_avatar`, `generic_document`,
  `project_cover`, `project_banner`, `monitoring_face_scan`, `biometric_reference_photo`,
  `monitoring_screenshot`, `objective_asset`, `task_attachment`, `task_description_image`.

### Every current upload site, and which pattern it uses

| Feature | Route | Storage pattern |
|---|---|---|
| Employee avatar | `PUT /employees/me/avatar` → `SetMyAvatarCommandHandler` | Denormalized FK: `Employee.AvatarFileId` |
| Legal entity logo | `PUT/GET/DELETE /legal-entities/{id}/logo` | Denormalized FK: `LegalEntity.LogoFileId` |
| Project cover/banner | Set at `POST /projects` create time, served via `GET {id}/logo`/`GET {id}/banner` | **Centralized**: `entity_assets` (`owner_type="project"`) |
| Task attachments / inline description images | `POST /tasks/pending-uploads` → linked into task via `TaskAssetLinker` on save | **Centralized**: `entity_assets` (`owner_type="task"`), "pending" (unlinked) then linked |
| Objective attachments | linked similarly to tasks | **Centralized**: `entity_assets` (`owner_type="objective"`) |
| Monitoring face-scan (Tray clock-in) | `UploadFaceScanCommandHandler` | Uploads via `IFileStorageService` (gets a real `file_records` row) then **copies `StorageKey` into its own `monitoring_face_scans.storage_key` string column** instead of an FK — a true denormalized duplicate |
| Biometric enrollment reference photo | `ValidateFacePhotoCommandHandler` | Clean FK: `BiometricProfile.ReferencePhotoFileId` |
| Periodic/inactivity screenshots | `SubmitPeriodicScreenshotCommandHandler` etc. | Clean FK: dedicated `monitoring_evidence_assets.FileRecordId` table (not `entity_assets`, but FK-based) |

No generic `/api/files/upload` or `/api/assets/upload` controller exists anywhere — every feature
has its own controller endpoint that internally calls the same `IFileStorageService`. The closest
existing generic pattern is the task "pending upload" flow: upload first (unlinked `file_records`
row), link to the owning entity in a second, permission-checked step.

### The profile photo bug, traced end to end

Upload genuinely works: `PUT /employees/me/avatar` → `SetMyAvatarCommandHandler` uploads to R2 and
sets `Employee.AvatarFileId`. The bug is entirely on the **read side**:

- `GetMyProfileQueryHandler.Handle` (`Queries/GetMyProfile/GetMyProfileQueryHandler.cs:84-89`)
  hard-codes `AvatarUrl = null` in `MyPersonalInformationResponse` and never reads
  `employee.AvatarFileId` at all. Unlike Projects/Legal Entities, there is **no `GET` endpoint**
  anywhere that resolves an employee's `AvatarFileId` into a viewable URL.
- Frontend (`profile.store.ts:57-75`, `uploadAvatar`) makes it worse: after the upload call
  succeeds it patches the **raw file ID** (a GUID) directly into `personalInformation.avatarUrl`,
  which `AvatarComponent` binds straight to `<img [src]>`. The image request 404s, the component's
  `error` handler falls back to initials, and the failure is silent.
- On the frontend, the file input's `(change)` already calls `store.uploadAvatar(file)`
  **immediately** on selection (`profile-tab.component.html:16` → `onAvatarSelected`) — independent
  of the "Save changes" button, which only submits the text fields
  (`UpdatePersonalInformationRequest` has no avatar field at all). So "Save changes" was never
  supposed to be involved in the photo upload; the modal's copy/UX just doesn't make that clear.

### Frontend upload UIs (5 sites, 4 different implementations, no shared component)

- `personal-info-form.component.ts` (avatar) — own `<input type="file">`, uploads on selection, no
  validation.
- `general-settings.component.ts` (legal entity logo) — own `<input type="file">`, uploads on
  selection, validates type + size (**hint text says "Max 2MB", code checks 5MB — a copy/logic
  mismatch**, noted for the fix but out of scope for this design).
- `project-form-modal.component.ts` (project logo/banner) — own `<input type="file">` x2, **staged
  locally**, bundled into the create-project multipart POST only on submit.
- `task-attachment-list.component.ts` (task attachments) — uploads immediately per file to
  `/tasks/pending-uploads`, referenced by id in the eventual task save payload.
- `asset-upload-list.component.ts` (objective assets, used by `sub-module-form.component.ts`) —
  drag-drop zone with staged/uploading/uploaded/error row states; closest existing thing to a
  reusable upload widget, but has exactly one consumer today.

No shared `FileUploadService`/`AssetUploadComponent`/`ImageCropperComponent` exists. Display-only
components that are already properly shared and should stay as-is: `AvatarComponent` (~15
consumers), `CompanyLogoTileComponent`.

## Goals

1. Fix the profile photo bug so a changed photo is visibly reflected immediately, without waiting
   on the larger migration.
2. Give every future upload feature one generic backend upload primitive instead of hand-rolling
   R2 calls, purpose validation, and quota checks per feature.
3. Extend the existing `entity_assets` centralization to Employee and Legal Entity, and fix
   `monitoring_face_scans`' string-duplication — closing the gap between "centralized" (Work
   Management) and "not centralized" (everything else).
4. Give the frontend one shared upload component/service so new upload features don't add a 5th
   bespoke implementation, and migrate the existing 5 onto it.

## Non-goals

- A universal, single HTTP endpoint that also decides *who* can attach a file to *what* — that
  authorization differs by feature (`employees:update-self`, `org:manage`, `projects:access`, …)
  and centralizing it into one generic policy risks a cross-tenant/cross-permission leak. See
  Architecture below for why this is deliberately split instead.
- Building a leave-request or chat-attachment feature — neither exists in this codebase yet, so
  there is nothing to migrate for them.
- Fixing the unrelated "Max 2MB" hint-text vs. 5MB actual-limit mismatch in the org logo uploader
  (noted above; a one-line copy fix, tracked separately, not part of this design).

## Architecture

Two layers, generalizing the pattern the task-attachment "pending upload" flow already proves
works, rather than inventing a new universal permission model:

**1. Generic upload primitive** — safe to centralize because it needs no entity-specific
authorization, only "authenticated tenant user + valid purpose + quota":
- `POST /api/v1/files` — multipart `{file, purpose}`. Validates `purpose` against the existing
  `UploadPurposeCatalog`, calls `IFileStorageService.UploadAsync`, returns `{fileId}`. The file
  exists in `file_records` but is not yet linked to anything ("pending", same status the task flow
  already uses).
- `DELETE /api/v1/files/{fileId}` — deletes a pending (unlinked) file the caller uploaded.
  Generalizes the existing `DELETE /tasks/pending-uploads/{fileId}`.

**2. Feature-specific link/resolve** — stays permission-checked per owning entity, because this is
exactly where authorization already differs today:
- Each feature endpoint shrinks to accepting `{fileId}` instead of raw multipart bytes, performs
  its own existing authorization check, then writes/updates an `entity_assets` row (`owner_type`,
  `owner_id`, `asset_purpose`, `is_primary`).
- One shared `GET /api/v1/files/{fileId}` replaces the bespoke `GetLogo`/`GetBanner`-style
  endpoints: resolves whether an `entity_assets` row links this file to an owner the caller can
  access (via a small per-owner-type `IEntityAssetAccessPolicy`, default-deny for unknown owner
  types), then redirects (302) to a signed R2 URL.

This keeps the security-sensitive part (who can see/attach what) exactly where each feature's
authorization already lives, while genuinely centralizing the repeated 90%: talk to R2, validate
purpose/size/type, enforce quota, write `file_records`, resolve to a signed URL.

## Data Model Changes

- Add `EntityAssetOwnerTypes.Employee` and `.LegalEntity` (string constants, same pattern as the
  existing three).
- New migration: backfill `entity_assets` rows from `employees.avatar_file_id` and
  `legal_entities.logo_file_id` — one row each, `asset_purpose = "employee_avatar"` /
  `"company_logo"`, `is_primary = true`.
- Two-step cutover, not a single destructive migration: (a) ship the backfill + new read path
  (resolve via `entity_assets` first, falling back to the legacy column if no `entity_assets` row
  exists yet — covers any write that happens between backfill and full cutover), verify in
  production, (b) a later migration drops `employees.avatar_file_id` and
  `legal_entities.logo_file_id` once verified. Both steps are part of this design; the second only
  ships after the first is confirmed clean, so it may land as a separate plan task with an explicit
  go/no-go rather than the same PR.
- `monitoring_face_scans` gets a new nullable `file_record_id` (Guid, FK → `file_records`),
  backfilled by matching existing `storage_key` values to `file_records.storage_key`, then a later
  migration drops the `storage_key` column once `UploadFaceScanCommandHandler` is updated to store
  the FK instead of copying the key.

## Backend API Changes

- New: `POST /api/v1/files`, `DELETE /api/v1/files/{fileId}`, `GET /api/v1/files/{fileId}`
  (`FilesController`, new).
- Changed: `PUT /employees/me/avatar` and `PUT /legal-entities/{id}/logo` accept `{fileId}` (JSON)
  instead of `IFormFile` multipart; internally now write an `entity_assets` row instead of setting
  `AvatarFileId`/`LogoFileId` directly (still sets the legacy column too, until the column-drop
  migration ships — see cutover note above).
- Removed once frontend migrates: `GET /legal-entities/{id}/logo`, `GET /projects/{id}/logo`,
  `GET /projects/{id}/banner` (superseded by the shared `GET /files/{fileId}`). Kept temporarily as
  thin wrappers around the new resolve endpoint until the frontend cutover (Phase 2) ships, then
  deleted.
- `GetMyProfileQueryHandler` starts resolving `AvatarUrl` (see Phase 0, below — this ships before
  the rest of this section, as a standalone fix using the *existing* per-feature endpoint pattern,
  not the new generic one).

## Frontend Changes

- One `UploadService` (`uploadFile(file, purpose)`, `deleteFile(fileId)`, plus a URL builder for
  `GET /files/{fileId}` — no separate "resolve" call needed since it's a redirect).
- One `<app-file-upload>` component:
  - `mode="immediate"` — upload + link right away (avatar, legal entity logo).
  - `mode="staged"` — hold the file locally, upload + link only when the parent form saves
    (project logo/banner; also becomes the new home for task attachments' existing
    upload-now/link-on-save behavior, unifying it with the visual component instead of task's
    bespoke pill-list UI).
  - `variant` input selects visual treatment (avatar circle / drop-zone / button) — one component,
    not four.
- Migrate all 5 existing sites (`personal-info-form`, `general-settings`, `project-form-modal`,
  `task-attachment-list`, `asset-upload-list`) onto it; delete their bespoke upload code once
  migrated. `AvatarComponent`/`CompanyLogoTileComponent` stay unchanged as pure display, now fed a
  URL built from the shared `UploadService` instead of ad hoc per-feature URL construction.

## Phased Delivery

- **Phase 0 (ship first — fixes the visible bug, no new architecture):**
  - Backend: add `GET /employees/me/avatar`, mirroring the existing `LegalEntitiesController.GetLogo`
    convention exactly (same signed-URL/redirect approach, no `entity_assets` involved yet).
    `GetMyProfileQueryHandler` builds `AvatarUrl` from it instead of hard-coding `null`.
  - Frontend: `profile.store.ts`'s `uploadAvatar` stops patching the raw file ID into state; it
    calls `loadProfile()` after a successful upload instead (matching every other store method's
    existing pattern), so the resolved URL from the new endpoint is what actually renders.
  - This phase alone is a small, isolated, low-risk change — implementable and shippable
    independent of everything below.
- **Phase 1 (backend centralization):** `FilesController` (`POST`/`DELETE`/`GET /files/...`),
  `EntityAssetOwnerTypes.Employee`/`.LegalEntity`, the `entity_assets` backfill migration, avatar
  and legal-entity-logo endpoints switched to the `{fileId}`-based contract, the
  `monitoring_face_scans.file_record_id` fix. The legacy `AvatarFileId`/`LogoFileId` columns and
  `GetLogo`/`GetBanner` endpoints are not removed in this phase — they're removed once Phase 2
  ships (see cutover note under Data Model Changes).
- **Phase 2 (frontend centralization):** `UploadService` + `<app-file-upload>`, migrate all 5
  upload sites, delete bespoke per-feature upload code, then the backend column/endpoint cleanup
  from Phase 1 can finally land.

## Testing Plan

Backend (xUnit): unit tests per new handler (`POST /files` purpose/quota validation, `DELETE
/files/{id}` ownership check on the caller, `GET /files/{id}` access-policy allow/deny per owner
type including a case proving cross-tenant/cross-owner denial), integration test for the
`entity_assets` backfill migration (row-for-row parity against pre-migration `AvatarFileId`/
`LogoFileId` values), and a regression test that `GetMyProfileQueryHandler` now returns a non-null
`AvatarUrl` after `SetMyAvatarCommandHandler` runs (Phase 0).

Frontend (Vitest): `profile.store.ts` avatar-upload-then-refetch behavior (Phase 0), `<app-file-upload>`
component tests for both `immediate` and `staged` modes, and updated specs for each of the 5
migrated call sites confirming they still emit the same eventual save payload shape.

## Out of Scope

- Building chat/message attachments or leave-request attachments — neither feature exists in this
  codebase yet.
- The org-logo "Max 2MB" hint-text vs. 5MB-actual-limit copy mismatch (flagged under Current State,
  fix is a one-line follow-up, not part of this design).
- Any change to per-feature authorization rules themselves (who may set an avatar, who may set a
  legal entity logo) — only how the file gets stored/linked/resolved changes, not who's allowed to
  do it.
