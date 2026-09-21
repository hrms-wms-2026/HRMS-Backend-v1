# Task Attachments, Text Highlighting & Inline Description Images — Design Spec

**Status:** Approved for planning
**Date:** 2026-09-14
**Repos touched:** `HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`
**Not touched:** `HRMS_TrayApp` (confirmed — it has no task description/attachment surface; it's a clock-in/monitoring agent only)

## Why

The Work Management task create/edit modal (`task-form-modal.component.ts`) already has a rich-text-looking description editor (Bold/Italic/Underline/Code/Lists/Link, a Write/Preview toggle) and an "Attachments" file picker with a "+ Add files" control and removable pills. None of it is backed by the server:

- `onFilesSelected` only ever captures `File.name` strings into `attachedFileNames` — the actual `File` objects are never uploaded, and `attachedFileNames` is never included in `CreateTaskRequestDto`/`EditTaskRequestDto`. Attachments are pure UI decoration today.
- There is no way to color or highlight text, and no way to insert an image into the description (ClickUp's task editor, given as reference, supports both).

## Scope

1. Real file upload/storage backing for the existing Attachments UI (create + edit).
2. An Attachments section on the task **edit** view (currently has none — anything attached at creation would otherwise be permanently unreachable).
3. Text color + highlight toolbar controls in the description editor.
4. Inline image insertion in the description editor.

## Out of scope (deferred, by explicit decision)

- **Approval-request flows.** `TaskCreationRequest`/`TaskEditRequest` (the path non-owners use, subject to owner approval) stay text-only. Attachments/images are wired only into the direct owner create/edit path (`CreateTaskCommand`, `EditTaskCommand`).
- **Background cleanup sweep.** Orphaned pending uploads (e.g. a browser crash or closed tab mid-compose) are not swept by a scheduled job. The frontend explicitly deletes a pending upload on pill removal and on modal cancel — that covers the common case. A stale pending upload from a crash is an accepted known limitation.
- Any change to `renderMarkdownOrHtml`'s existing regex-based preview transform beyond confirming it doesn't mangle the new `<span style="...">`/`<img>` markup (it doesn't — see "Preview rendering" below).

---

## 1. Backend — shared storage infrastructure additions

Two small, symmetric additions to the existing quota-enforced file storage subsystem (`IFileStorageService` / `IStorageQuotaService`), used by every upload feature in the app. **No database migration is required** — `file_records.DeletedAt`/`StorageDeletedAt` and `tenant_storage_stats` already exist for exactly this; nothing before this feature ever wired a delete path to them.

```csharp
// IFileStorageService
Task<Result> DeleteAsync(Guid tenantId, Guid userId, Guid fileRecordId, CancellationToken ct = default);
```
- Loads the `file_records` row for the tenant; 404 if missing or already soft-deleted.
- Caller-ownership is enforced by the caller (feature handler), not by this method — consistent with `OpenReadAsync`'s existing documented trust model.
- Best-effort deletes the R2 object (`IObjectStorageAdapter.DeleteObjectAsync`), sets `DeletedAt`/`StorageDeletedAt`, and calls the new quota release below. An R2 delete failure is logged but does not fail the call (mirrors the existing compensating-delete pattern in `CompleteUploadAsync`'s rollback path) — the row is still marked deleted so it stops counting as "live" to the rest of the app even if the object lingers in R2.

```csharp
// IStorageQuotaService
Task<Result> ReleaseUsedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default);
```
- Symmetric to the existing `ReleaseReservedStorageAsync`, but decrements `used_r2_bytes` instead of `reserved_r2_bytes`. Always succeeds, idempotent floor at zero, same as its reserved-side counterpart.

## 2. Backend — new upload purposes & owner type

`UploadPurposeCatalog` gains two entries:

| Purpose | Allowed types | Max size |
|---|---|---|
| `task_attachment` | pdf, png/jpg/jpeg/webp/gif, doc/docx, xls/xlsx, zip | 25 MB |
| `task_description_image` | png/jpeg/webp | 5 MB |

`EntityAssetOwnerTypes` gains `Task`. `EntityAsset` rows link a `WorkTask.Id` to a `FileRecord.Id` with `AssetPurpose` set to one of the two purposes above, `IsPrimary = false` (multiple per task, unlike the single-logo project pattern this reuses).

## 3. Backend — new endpoints (`TasksController`)

```
POST   api/v1/work/tasks/pending-uploads          (multipart: file, purpose)
DELETE api/v1/work/tasks/pending-uploads/{fileId}
GET    api/v1/work/tasks/files/{fileId}
```

**`POST pending-uploads`** — validates `purpose ∈ {task_attachment, task_description_image}`, then calls the existing `IFileStorageService.UploadAsync` (which itself validates content-type/size against the purpose rule and enforces quota). Returns `{ fileId, originalFileName, fileSizeBytes, contentType }`. The file is *not* linked to any task yet — it's a bare `file_records` row owned (via `UploadedByUserId`) by the caller.

**`DELETE pending-uploads/{fileId}`** — loads the file record, 404s if missing, 403s if `UploadedByUserId` isn't the caller, 409s if an `EntityAsset` already links it (already-linked files are removed via the edit-time attachment-list diff instead, see §5). Otherwise calls the new `DeleteAsync`.

**`GET tasks/files/{fileId}`** — streams bytes (`OpenReadAsync`). Access check: allow if either (a) an `EntityAsset` links this `fileId` to a `WorkTask` the caller can access (same visibility rule `GetTaskByIdQuery` already uses), or (b) the file is unlinked and `UploadedByUserId` is the caller (covers the compose-time preview, before the task exists). Otherwise 404 (never leak existence).

This one content endpoint serves both attachment downloads and inline description `<img>` tags — the frontend's session cookie (`withCredentials: true`, confirmed from the existing project-logo `<img src>` pattern) is sent automatically by the browser on a plain `<img>` GET, so no signed URLs or blob-fetch dance is needed.

## 4. Backend — CreateTask / EditTask changes

`CreateTaskCommand` and `EditTaskCommand` (and their `CreateTaskRequest`/`EditTaskRequest` API contracts) gain:

```csharp
IReadOnlyList<Guid>? AttachmentFileIds
```

**On create:** after the `WorkTask` row is inserted, for each id in `AttachmentFileIds` that resolves to a `file_records` row in this tenant uploaded by the caller and not already linked elsewhere, create an `EntityAsset(purpose: task_attachment)`. Ids that fail validation are silently skipped (not fatal — a stale/removed pending upload shouldn't block creating the task).

**On edit:** `AttachmentFileIds` represents the *full desired set* after the edit (existing pills the user kept + any newly uploaded ones). The handler diffs against the task's current `task_attachment` `EntityAsset` rows: new ids get linked (same validation as create), ids no longer present get their `EntityAsset` removed **and** their file deleted via `DeleteAsync` (releases quota — an attachment explicitly removed shouldn't sit around consuming storage forever).

**Description image scanning (both create and edit):** the description HTML is scanned with a plain regex for `tasks/files/([0-9a-f-]{36})` references (no HTML parsing needed — this is our own fixed URL shape). Matched ids are linked as `task_description_image` `EntityAsset`s the same way attachments are; on edit, `task_description_image` links whose id is no longer referenced in the new description text are removed and their file deleted, the same way dropped attachments are. This means removing an inline image from the text is sufficient cleanup — no separate "delete inline image" endpoint.

## 5. Backend — read side

`GetTaskByIdQuery`'s response gains:

```csharp
IReadOnlyList<TaskAttachmentDto> Attachments  // { FileId, FileName, FileSizeBytes, ContentType }
```
sourced from `IEntityAssetRepository.ListByOwnerAsync(tenantId, EntityAssetOwnerTypes.Task, taskId)` filtered to `task_attachment` purpose (description-image assets aren't surfaced here — they're already visible inline in the rendered description).

Board/list queries (`GetProjectTasksQuery`, `GetObjectiveTasksQuery`, `GetMyProjectTasksQuery`) are **not** changed — they don't need per-task attachment joins for a Kanban board render, and adding one would be an N+1 cost for no product value at this scope.

---

## 6. Frontend — attachment upload wiring

`task-form-modal.component.ts` replaces `attachedFileNames = signal<string[]>([])` (cosmetic-only today) with:

```ts
attachedFiles = signal<{ fileId: string; name: string; sizeBytes: number; uploading?: boolean }[]>([])
```

- `onFilesSelected`: for each selected `File`, push a placeholder row with `uploading: true`, call a new `TaskApiService.uploadPendingFile(file, 'task_attachment')`, then fill in `fileId`/`sizeBytes` on success or drop the row and surface an inline error on failure.
- `removeAttachedFile(fileId)`: optimistically removes the pill and calls `TaskApiService.deletePendingFile(fileId)` best-effort (edit-mode pills that were already-linked at load time are just dropped from the local set — their removal is applied server-side by the edit-time diff in §4, not by this delete-pending call, since they aren't "pending" from the API's point of view).
- Modal close/cancel (`onBackdrop`/`onEscape`/Cancel button) in **create** mode: for any `attachedFiles()` entries that are still unlinked (i.e. the create was never submitted), fire-and-forget `deletePendingFile` for each — this is the "delete on cancel" behavior.
- `submit()` / `submitCreateWithAction()`: include `attachmentFileIds: this.attachedFiles().map(f => f.fileId)` in the request payload.

**Edit view** gets a new "Attachments" card in `tfm__pane-side` (same visual family as the existing Time Tracking / Activity Log cards): lists `task().attachments` on load (seeded into `attachedFiles`), same add/remove pills, same "+ Add files" control. Removing a pill here stages the removal — like every other edit-view field in this modal, it's applied on Save, not instantly (consistent with existing UX; there's no autosave anywhere else in this component either).

`TaskApiService` gains:
```ts
uploadPendingFile(file: File, purpose: 'task_attachment' | 'task_description_image'): Observable<PendingUploadDto>
deletePendingFile(fileId: string): Observable<void>
getFileUrl(fileId: string): string   // `${base}/files/${fileId}` — direct <img>/download src
```

## 7. Frontend — text color & highlight

Two new toolbar buttons added after Underline (before the existing Code/Lists divider), each opening the same small popover component the toolbar already uses for the link/assignee/priority pickers (`tfm-popover` styling, `activePopover`-style open/close plumbing):

- **Text color** — 8 swatches (default + red/orange/yellow/green/blue/purple/pink, matching the ClickUp reference) plus a "Remove color" entry at the top. Applies via `document.execCommand('foreColor', false, hex)` to the current selection.
- **Highlight** — the same 8 swatches (at a lighter tint) plus "Remove highlight", applied via `document.execCommand('hiliteColor', false, hex)`.

Both call `onEditorInput()` afterward to sync the `description` signal, exactly like the existing `formatDoc()` calls for Bold/Italic/Underline — this editor already exclusively uses `execCommand` for all formatting, so this follows the file's existing convention rather than introducing a separate rich-text engine.

**Preview rendering:** `renderMarkdownOrHtml`'s regex passes (bold/italic/code/lists/links/checklists) only match markdown-style tokens (`**`, `` ` ``, `- [ ]`, etc.) — a `<span style="color:#e03131">…</span>` or `<img src="...">` emitted by `execCommand` passes through untouched, so no changes are needed there. (The existing renderer's habit of regexing raw HTML rather than parsing it is a pre-existing fragility — e.g. a literal `*` inside an unrelated attribute could misfire — but that's pre-existing behavior, not something this feature introduces or is in scope to fix.)

## 8. Frontend — inline images

One more toolbar button ("Insert image") opens a hidden `accept="image/png,image/jpeg,image/webp"` file input. On selection:

1. Save the current selection range (`window.getSelection().getRangeAt(0)`) before the async upload starts, the same way the existing link-insertion flow must already preserve the caret/selection across the popover interaction.
2. Upload via `uploadPendingFile(file, 'task_description_image')`.
3. Restore the saved range, then `document.execCommand('insertImage', false, taskApi.getFileUrl(fileId))`.
4. `onEditorInput()` to sync `description`.

No client-side resizing/cropping — out of scope. A failed upload leaves the editor untouched and surfaces an inline error next to the toolbar (same treatment as a failed attachment upload).

---

## Testing

**Backend:**
- Unit tests for `FileStorageService.DeleteAsync` and `StorageQuotaService.ReleaseUsedStorageAsync` (happy path, already-deleted idempotency, quota floor at zero).
- Unit tests for `CreateTaskCommandHandler`/`EditTaskCommandHandler`: attachment linking, edit-time diff (add/remove), description-image regex scan (link on add, unlink+delete on removal from text), invalid/foreign id is skipped not fatal.
- Integration test: pending-upload → create task with `attachmentFileIds` → `GetTaskById` shows the attachment → `GET tasks/files/{fileId}` streams it.
- Integration test: access-control on `GET tasks/files/{fileId}` (linked-but-no-task-access → 404; unlinked-and-not-uploader → 404).

**Frontend:**
- `task-form-modal.component.spec.ts` updates: upload-wiring for `onFilesSelected`/`removeAttachedFile` (mocked `TaskApiService`), cancel-triggers-cleanup behavior, payload shape (`attachmentFileIds` present on submit).
- New spec coverage for the color/highlight popovers and image-insertion button (`execCommand` calls, selection save/restore).
- Manual verification in the browser preview: create a task with an attachment + a colored/highlighted description + an inline image, reload, confirm the edit view shows the attachment and the description renders identically.
