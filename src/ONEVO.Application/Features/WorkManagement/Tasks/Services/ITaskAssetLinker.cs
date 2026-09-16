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
}
