# Task Comments — Design Spec

**Status:** Approved for planning
**Date:** 2026-09-22
**Repos touched:** `HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`
**Not touched:** `HRMS_TrayApp`

## Why

The task detail view (`task-form-modal.component.ts`, edit pane) has no way for teammates to discuss a task inline — no comments, no reactions, no per-task discussion thread. Everything a comment needs already exists as infrastructure elsewhere in Work Management and should be reused, not rebuilt:

- The rich-text editor (Bold/Italic/Underline/color/highlight, inline images) built for the task description field in the 2026-09-14 attachments/rich-description feature.
- The generic file-upload pipeline (`IFileStorageService`, pending-uploads, `EntityAsset` owner-type linking) the same feature introduced.
- The unified Task History feed (`GetTaskHistoryQuery`) that already merges edit-logs, status-change-logs, clock-sessions and percentage-change-logs into one timeline on the task detail page.

## Scope

1. Comments on a task: post, edit (author-only), soft-delete (author-only), reply (one level, YouTube-style — replying to a reply still attaches under the original top-level comment).
2. Rich content in comments: same formatting + inline images as the task description editor, plus file attachments (reusing the existing attachment pipeline).
3. Emoji reactions on a comment: a default quick-set of 4, plus a full emoji picker; a user may stack multiple distinct emoji on the same comment, but not the same emoji twice.
4. An audit trail of comment edits/deletes (who + when + action, **not** the changed content) that appears in the existing unified Task History feed as a new entry type — no separate log page.
5. Anyone who can view the task (same visibility rule `GetTaskById` already enforces) can comment, reply and react. Only the comment's author can edit or delete it — no owner override.

## Out of scope (deferred, by explicit decision)

- Nested replies beyond one level (a reply cannot itself be replied to as a sub-thread; it attaches flat under the top-level comment, matching YouTube).
- Logging comment *creation* in Task History — only edits and deletes are logged; a new comment is already visible as itself.
- Notifications/mentions on comment/reply (no `@mention` push notification wiring) — out of scope for this pass.
- Any change to `TaskCreationRequest`/`TaskEditRequest` approval-flow forms — comments are a direct feature on an existing task, unrelated to the create/edit approval path.
- Background cleanup of orphaned pending comment-attachment uploads — same accepted limitation as the description-image feature (frontend deletes on cancel/remove; a crash mid-compose leaves a stale pending upload).

---

## 1. Backend — new tables

Three new tables, all `BaseEntity`-derived with standard tenant RLS policy coverage (same pattern as `TaskEditLog`/`TaskStatusChangeLog`):

```csharp
// ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskComment
public class TaskComment : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ParentCommentId { get; set; }   // null = top-level; set = reply (always references a top-level comment)
    public string Content { get; set; } = "";     // rich HTML, same convention as WorkTask.Description
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
```

```csharp
// ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCommentLog
public static class TaskCommentLogActions
{
    public const string Edited = "edited";
    public const string Deleted = "deleted";
}

public class TaskCommentLog : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid CommentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Action { get; set; } = TaskCommentLogActions.Edited;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
```

```csharp
// ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskCommentReaction
public class TaskCommentReaction : BaseEntity
{
    public Guid CommentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Emoji { get; set; } = "";
    // unique index (CommentId, EmployeeId, Emoji)
}
```

Soft delete: a comment with existing replies is kept as `IsDeleted = true` and rendered as a "[comment deleted]" tombstone (content blanked client-side, replies stay attached underneath). A comment with no replies is simply removed from the list by the frontend once deleted — the row still exists soft-deleted server-side for audit consistency, but the read side filters replyless soft-deleted comments out entirely.

No table is needed for attachments/inline images on comments — see §2.

## 2. Backend — reuse of existing attachment infrastructure

`EntityAssetOwnerTypes` gains:
```csharp
public const string Comment = "comment";
```

`UploadPurposeCatalog` gains two entries, mirroring the task ones exactly (same size/type rules):

| Purpose | Allowed types | Max size |
|---|---|---|
| `comment_attachment` | pdf, png/jpg/jpeg/webp/gif, doc/docx, xls/xlsx, zip | 25 MB |
| `comment_description_image` | png/jpeg/webp | 5 MB |

`POST api/v1/work/tasks/pending-uploads` (existing endpoint) accepts these two new purpose values in addition to the task ones already handled — no new upload endpoint needed. `GET api/v1/work/tasks/files/{fileId}` (existing) gains a second access-check branch: allow if an `EntityAsset` links this `fileId` to a `TaskComment` the caller can view (same task-visibility rule, resolved via the comment's `TaskId`).

Comment content HTML is scanned for inline `tasks/files/{id}` references the same way task descriptions are (§4 of the 2026-09-14 spec) to link/unlink `comment_description_image` assets on post/edit.

## 3. Backend — new `CommentsController` (`api/v1/work`)

A dedicated controller (comments are numerous, nested and reaction-bearing enough to warrant separation from the already-large `TasksController`):

```
GET    tasks/{taskId}/comments              list: top-level comments, each with its replies, reactions and attachments nested
POST   tasks/{taskId}/comments              create top-level comment { content, attachmentFileIds }
POST   comments/{id}/replies                create reply { content, attachmentFileIds } — 400 if {id} is itself a reply
PATCH  comments/{id}                        edit { content, attachmentFileIds } — author-only, 403 otherwise
DELETE comments/{id}                        soft-delete — author-only, 403 otherwise
POST   comments/{id}/reactions              add reaction { emoji } — idempotent no-op if it already exists
DELETE comments/{id}/reactions/{emoji}      remove caller's own reaction of that emoji
```

Every endpoint resolves the comment's task and applies the exact visibility check `GetTaskByIdQueryHandler` already uses (`projects:read` permission, else membership via `GetActiveObjectiveIdsForEmployeeInProjectAsync` against the task's `ObjectiveId`) before allowing read, post, reply or react. Edit/delete add the author check on top (`comment.EmployeeId == callerEmployeeId`).

Reply validation: `POST comments/{id}/replies` 400s if the target comment's own `ParentCommentId` is non-null (can't reply to a reply) — this is what keeps the thread flat at one level.

Edit/delete on a comment write a `TaskCommentLog` row (`Edited`/`Deleted`) in the same handler transaction as the content change.

## 4. Backend — read side (`GetCommentsForTaskQuery`)

Response shape:
```csharp
public sealed record TaskCommentResponse(
    Guid Id, Guid EmployeeId, string EmployeeName, string Content, bool IsEdited, bool IsDeleted,
    DateTimeOffset CreatedAt, IReadOnlyList<TaskCommentReactionDto> Reactions,
    IReadOnlyList<TaskAttachmentDto> Attachments, IReadOnlyList<TaskCommentResponse> Replies);

public sealed record TaskCommentReactionDto(string Emoji, IReadOnlyList<Guid> EmployeeIds); // grouped, for the reaction bar
```

Handler loads all `TaskComment` rows for the task (top-level + replies in one query, grouped in memory by `ParentCommentId`), replyless soft-deleted top-level comments filtered out, reactions grouped by emoji, attachments joined via `EntityAsset` the same way `GetTaskByIdQueryHandler` already does for task attachments. Employee display names resolved in one batch via `ICallerIdentityResolver`, same pattern as `GetTaskHistoryQueryHandler`.

## 5. Backend — Task History integration

`TaskHistoryEntryTypes` gains:
```csharp
public const string Comment = "comment";
```
`TaskHistoryEntryResponse` gains a fifth optional details slot:
```csharp
public sealed record TaskCommentLogEntryDetails(Guid CommentId, string Action);
```
`GetTaskHistoryQueryHandler` gains an `ITaskCommentLogRepository.GetForTaskAsync` call, folded into the existing `entries` merge/sort exactly like the other four log sources — no new endpoint, no new frontend log surface. Comment creation is intentionally not logged here (§ Out of scope).

---

## 6. Frontend — Comments card

New "Comments" card in `task-form-modal.component.ts`'s edit-view pane (`tfm__pane-side`), alongside the existing Attachments/Time Tracking/Activity Log cards:

- **Compose box** at the top: reuses the existing rich-text toolbar component/`execCommand` wiring and attachment-pill/inline-image flow built for the description field (§6-8 of the 2026-09-14 spec), scoped to `comment_attachment`/`comment_description_image` purposes, in a shorter compose box.
- **Comment list**: top-level comments sorted newest-first by `CreatedAt` (matches the existing Task History card's `OccurredAt`-descending order), each rendering:
  - author name/avatar, relative timestamp, an "(edited)" tag when `isEdited`
  - rendered rich content (same `renderMarkdownOrHtml`-style pass already used for the description)
  - reaction bar: the 4 default emoji quick-buttons + a "+" opening a full emoji picker; each reaction pill shows a count and highlights if the caller is one of the reactors, click toggles caller's own reaction
  - "Reply" link that opens an inline compose box; replies render flat, indented once, directly under the top-level comment (no reply-to-reply UI)
  - Edit/Delete controls, visible only when `comment.employeeId === currentEmployeeId`; Edit swaps the rendered content for the same compose editor pre-filled with the existing content; Delete confirms then calls the delete endpoint and removes the comment from the local list (or replaces it with the tombstone if it has replies)
The card is a standalone `TaskCommentsComponent` (its own `.ts`/`.html`/`.spec.ts`), not inlined into `task-form-modal.component.ts` — the comment tree (compose, list, replies, reactions) is substantial enough to warrant its own file, unlike the smaller Attachments pill-list. It receives the task id as an input and is mounted inside `tfm__pane-side`.

A new `TaskCommentApiService` gains `getComments`, `postComment`, `postReply`, `editComment`, `deleteComment`, `addReaction`, `removeReaction`, plus reuse of the existing `uploadPendingFile`/`deletePendingFile`/`getFileUrl` methods (from `TaskApiService`) with the two new purpose strings.

---

## Testing

**Backend:**
- Unit tests per handler: `CreateComment`, `CreateReply` (400 on reply-to-reply), `EditComment`/`DeleteComment` (author-only 403, writes `TaskCommentLog`), `AddReaction`/`RemoveReaction` (idempotency, multi-emoji stacking, unique-per-emoji-per-user).
- `GetCommentsForTaskQueryHandler` tests: nesting/grouping, reaction grouping, replyless-soft-deleted filtering, tombstone-with-replies retained.
- `GetTaskHistoryQueryHandler` test: comment edit/delete entries appear in the merged, sorted feed.
- Integration tests: full post → reply → react → edit → delete flow against `GET tasks/{id}/comments`; access-control (no task visibility → 404 on every comment endpoint; non-author edit/delete → 403); attachment upload → comment → `GET tasks/files/{fileId}` access-check covers comment-linked files.

**Frontend:**
- New `TaskCommentsComponent` spec: render nesting, reaction toggling, author-only edit/delete gating, reply compose flow, tombstone rendering for has-replies deletes.
- `task-form-modal.component.spec.ts` gets a small addition confirming `TaskCommentsComponent` is mounted with the correct task id input.
- Manual verification in the browser preview: post a comment with formatting/attachment/inline image, reply to it, react with a default and a picker emoji, edit and delete both a replyless and a has-replies comment, confirm the Task History card shows the edit/delete entries.
