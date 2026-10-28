# Module / Sub-Module Creation Screen — Assets, Members, Date Range Picker

Date: 2026-09-02 (revised 2026-09-02 after implementation-planning research — see "Revision" notes below)
Status: Approved
Repos affected: `HRMS-Backend-v1` (this repo — asset storage/API), `Hrms--Web-application---front-end---v1` (companion copy of this spec — screen redesign)

**Base branches (neither repo has this feature area on `main`):**
- Backend: `feature/wm-approval-hours-and-component-tuning`
- Frontend: `feature/employee-management-phase1-foundation`

## Goal

Redesign the module/sub-module creation & edit screen (`sub-module-form.component.ts`, used for both top-level modules and sub-modules via `parentObjectiveId`) to match a reference design, with two deliberate deviations from that reference:

1. Asset uploads are consolidated into **one** upload section (documents, images, ZIP files together) instead of four separate cards.
2. The start/end date fields are kept (the reference design dropped them) — swapped onto the app's existing shared date-range-picker component rather than removed.

A Members section (search + add + designate one owner) is also added, reusing existing frontend components.

**Out of scope:** external "Links" attachments (URL-only, no file) — dropped per decision; not part of this feature.

## Current state (as of investigation)

- `CreateObjectiveRequestDto` / `CreateObjectiveCommand` already carry `HeadEmployeeId?` and `MemberInvitations? : List<{EmployeeId, Type}>` — members are already a backend-supported concept, just not wired into this form's UI.
- No asset/file support exists anywhere for objectives today: no join table to `file_records`, no endpoint, no registered upload "purpose".
- No generic file-upload/dropzone component exists in the frontend to reuse; the consolidated upload UI is new.
- A shared `app-date-range-picker` component (`from`/`to` inputs, `rangeChange` output) already exists and is used elsewhere (e.g. time-tracking); the form currently uses two plain `type="date"` inputs instead of it.
- `employee-picker` + `member-management-popup` already implement search/add/owner-designation UX, currently wired into `project-form-modal.component.ts`'s create flow (not into objective/sub-module creation).

## Backend design

### Revision (found during implementation planning)

The original plan below assumed a brand-new `objective_assets` join table. Research turned up an existing generic, tenant-scoped, polymorphic file-attachment table already in the schema — `entity_assets` (entity: `EntityAsset`, in `ONEVO.Domain.Features.Storage.EntityAssets`), keyed by `OwnerType` + `OwnerId` + `AssetPurpose` + `FileRecordId`, currently used only for Project logo/banner (`EntityAssetOwnerTypes.Project = "project"`). It has **no DB-level constraint tying it to `"project"`** — `OwnerType` is a plain indexed `varchar(50)`. Reusing it for objective assets means:

- **No new migration/table at all.** Just add `Objective = "objective"` to `EntityAssetOwnerTypes` (`src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs`) and a new purpose constant (e.g. `ObjectiveAsset`) to `UploadPurposeCatalog`.
- Two new methods needed on `IEntityAssetRepository` (currently only has `AddAsync` and `GetPrimaryFileIdsByOwnerAsync`, which assumes one "primary" asset per owner — not our multi-file case): a list-by-owner query and a delete-by-id method, implemented in `EfEntityAssetRepository`.
- Uploads still go through the same `IFileStorageService.UploadAsync`, same as every other upload feature (Cloudflare R2-backed).

This is strictly less work than the original table and more consistent with how the codebase already models file attachments — adopted as the design.

### New file-storage purpose

Register `"objective-asset"` as a purpose with `IFileStorageService`/`UploadPurposeCatalog`.

### New endpoints

Route prefix confirmed as `api/v1/work/objectives` (not `api/objectives` as originally assumed), matching `ObjectivesController`'s existing `[Route("api/v1/work/objectives")]`.

- `POST /api/v1/work/objectives/{id}/assets` — `[FromForm]`, multipart, one file per call (mirrors `ProjectsController.Create`'s `[FromForm] CreateProjectFormRequest` + `IFormFile` pattern). Uploads via `IFileStorageService.UploadAsync` with purpose `objective-asset`, inserts an `entity_assets` row (`OwnerType = "objective"`, `OwnerId = objectiveId`, `IsPrimary = false`), returns `{id, fileName, sizeBytes, contentType, uploadedAt, downloadUrl}`.
- `DELETE /api/v1/work/objectives/{id}/assets/{assetId}` — deletes the `entity_assets` row only (the underlying `file_records` row is never touched from feature code anywhere in this codebase — confirmed via `RemoveLegalEntityLogoCommandHandler`, which only clears an FK and explicitly comments that file cleanup is out of scope for feature handlers). Same convention here.
- `GET /api/v1/work/objectives/{id}` (existing detail endpoint) — response gains an `assets: ObjectiveAssetResponse[]` array, populated only in `GetObjectiveByIdQueryHandler` (the create-flow response can default to an empty array, since assets are uploaded via a separate call after creation — see Frontend design below).

**Why per-asset endpoints instead of extending `CreateObjectiveRequest` to multipart:** keeps the existing create/update contracts untouched (no breaking change for other callers), and the same two endpoints serve both the create flow (called right after the objective is created) and the edit flow (called immediately on file pick) — one code path instead of two.

**`IFileStorageService` has no delete method** — confirmed by reading the interface. The delete endpoint above only ever removes the join row, matching every existing "remove attachment" handler in this codebase.

### Validation defaults (confirmed)

- Extension allow-list: `pdf, doc, docx, xls, xlsx, png, jpg, jpeg, gif, zip`
- Per-file size cap: 25MB
- Reject with a clear error on the upload endpoint if either check fails; no server-side virus scan in scope (matches existing upload features' current behavior).

## Frontend design

### Form layout

`sub-module-form.component.ts` gains two new sections (Assets, Members) alongside the existing Title/Description/Allocated Hours fields, and the date fields are swapped as described below. Layout follows the reference image's visual structure (cards/sections stacked in the modal) but with one asset section instead of four.

### Asset upload (new component)

One dropzone + "Browse files" button accepting documents/images/ZIP together. Selected/uploaded files render as a single list of rows: type icon, name, size, status (uploading/done/error+retry), remove button.

**Create mode:** files are staged locally (native `File` objects, nothing uploaded yet) until Submit. On submit: create the objective via the existing (unchanged) JSON endpoint, then upload each staged file via `POST /objectives/{id}/assets`, showing per-file progress. If a file upload fails, the objective still exists (already created) — show an inline retry per failed file rather than rolling back.

**Edit mode:** the objective already exists, so files upload immediately on selection via the same endpoint; removing an asset calls `DELETE /objectives/{id}/assets/{assetId}` immediately (no "save to confirm" step).

### Members section — **create mode only**

Reuse the `app-employee-picker` pattern exactly as `project-form-modal.component.ts` drives it (non-inline modal, `pickerOpen` signal, `onMemberPicked`): search opens the picker, selected members render as cards, clicking a card's "Owner" toggle sets `headEmployeeId` locally; everyone else becomes a `memberInvitations` entry on submit. Both fields already exist on `CreateObjectiveRequestDto` (confirmed unused by any current caller — this feature is the first) — frontend wiring only, no backend change.

**Scope boundary:** `EditObjectiveRequestDto` has no `headEmployeeId`/`memberInvitations` fields, and editing an objective's membership already has a separate, established flow (`app-member-management-popup` with `scope="objective"`, wired into `milestone-tree-tab.component.html` outside this form). The Members section is added to `sub-module-form` **only in `mode="create"`** — edit mode (`mode="edit"`, titled "Module settings") keeps using the existing separate member-management popup, not duplicated inside this form. No backend change to `EditObjectiveRequestDto` is in scope.

### Date fields

Replace the two native `type="date"` inputs with the existing shared `app-date-range-picker` (`from`/`to` signals, `rangeChange` output) — same component/behavior already used elsewhere in the app (e.g. time-tracking), consolidated into one control instead of two separate fields. Validation (end ≥ start) stays enforced the same way it is today.

## Testing

- Backend: unit tests for the new command/handlers (upload asset, delete asset), integration test for the full create-objective → upload-asset → fetch-detail round trip. No migration to test — `entity_assets` already exists.
- Frontend: component tests for the new asset-upload component (stage/upload/remove/error-retry states in both create and edit mode), and for the members wiring (owner toggle, invitation list building) — following the existing test patterns already used for `sub-module-form.component.ts` and `project-form-modal.component.ts`.

## Assumptions carried into implementation

- 25MB/file size cap and the extension allow-list above are defaults, not explicitly requested — flagged here as the recorded assumption if they need revisiting later.
- No file count limit assumed beyond what's reasonable for a modal list UI (not enforced server-side unless a concrete need surfaces during implementation).
