# Module / Sub-Module Creation Screen — Assets, Members, Date Range Picker

Date: 2026-09-02
Status: Approved
Repos affected: `HRMS-Backend-v1` (this repo — asset storage/API), `Hrms--Web-application---front-end---v1` (companion copy of this spec — screen redesign)

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

### New table: `objective_assets`

Additive-only migration, no existing tables modified:

| column | type | notes |
|---|---|---|
| `id` | uuid, PK | |
| `objective_id` | uuid, FK → objectives | cascade delete with parent objective |
| `file_record_id` | uuid, FK → `file_records` | |
| `uploaded_by_employee_id` | uuid | |
| `created_at` | timestamptz | |

RLS policy mirrors the tenant-isolation pattern already used for `file_records` / `file_upload_reservations`.

### New file-storage purpose

Register `"objective-asset"` as a purpose with the existing `IFileStorageService` (the same service every other upload feature — avatars, legal-entity logos, face scans — already goes through; storage backend is Cloudflare R2). No new storage infrastructure needed.

### New endpoints

- `POST /api/objectives/{id}/assets` — multipart, one file per call. Uploads via `IFileStorageService.UploadAsync` with purpose `objective-asset`, inserts the `objective_assets` join row, returns `{id, fileName, sizeBytes, contentType, uploadedAt, downloadUrl}`.
- `DELETE /api/objectives/{id}/assets/{assetId}` — removes the join row (follow the same soft/hard-delete convention as `RemoveLegalEntityLogoCommandHandler`).
- `GET /api/objectives/{id}` (existing detail endpoint) — response gains an `assets: AssetDto[]` array.

**Why per-asset endpoints instead of extending `CreateObjectiveRequest` to multipart:** keeps the existing create/update contracts untouched (no breaking change for other callers), and the same two endpoints serve both the create flow (called right after the objective is created) and the edit flow (called immediately on file pick) — one code path instead of two.

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

### Members section

Reuse the existing `employee-picker` + `member-management-popup` pattern already proven in `project-form-modal.component.ts`: search box opens the picker, selected members render as cards, clicking a card's "Owner" toggle sets `headEmployeeId` locally; everyone else becomes a `memberInvitations` entry on submit. Both fields already exist on `CreateObjectiveRequestDto` — this is frontend wiring only, no backend change needed for members.

### Date fields

Replace the two native `type="date"` inputs with the existing shared `app-date-range-picker` (`from`/`to` signals, `rangeChange` output) — same component/behavior already used elsewhere in the app (e.g. time-tracking), consolidated into one control instead of two separate fields. Validation (end ≥ start) stays enforced the same way it is today.

## Testing

- Backend: unit tests for the new command/handlers (upload asset, delete asset), integration test for the full create-objective → upload-asset → fetch-detail round trip, migration up/down check.
- Frontend: component tests for the new asset-upload component (stage/upload/remove/error-retry states in both create and edit mode), and for the members wiring (owner toggle, invitation list building) — following the existing test patterns already used for `sub-module-form.component.ts` and `project-form-modal.component.ts`.

## Assumptions carried into implementation

- 25MB/file size cap and the extension allow-list above are defaults, not explicitly requested — flagged here as the recorded assumption if they need revisiting later.
- No file count limit assumed beyond what's reasonable for a modal list UI (not enforced server-side unless a concrete need surfaces during implementation).
