namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>
/// Resyncs a task's linked files (attachments and inline description images)
/// to a desired end state. Used identically by CreateTask (starting from
/// nothing) and EditTask (diffing against whatever is already linked).
/// Never fails the caller — invalid, foreign, or already-claimed file ids are
/// silently skipped rather than surfaced as an error.
/// </summary>
public interface ITaskAssetLinker
{
    Task SyncAttachmentsAsync(
        Guid tenantId, Guid userId, Guid taskId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default);

    Task SyncDescriptionImagesAsync(
        Guid tenantId, Guid userId, Guid taskId, string? descriptionHtml, CancellationToken ct = default);

    Task SyncCommentAttachmentsAsync(
        Guid tenantId, Guid userId, Guid commentId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default);

    Task SyncCommentDescriptionImagesAsync(
        Guid tenantId, Guid userId, Guid commentId, string? contentHtml, CancellationToken ct = default);

    /// <summary>Copies every TaskAttachment-purpose asset from the source task to the destination task as
    /// genuinely new file records (re-uploaded via IFileStorageService), each linked only to the destination.
    /// A file record can only ever be linked to one owner (see SyncAsync), so this cannot reuse the source's
    /// file ids - doing so would silently no-op instead of attaching anything to the copy. Best-effort per
    /// file: a source file that no longer opens or re-uploads is skipped rather than failing the whole copy.</summary>
    Task CopyAttachmentsAsync(
        Guid tenantId, Guid userId, Guid sourceTaskId, Guid destinationTaskId, CancellationToken ct = default);
}
