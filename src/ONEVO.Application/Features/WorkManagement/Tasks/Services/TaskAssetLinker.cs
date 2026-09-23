using System.Text.RegularExpressions;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public sealed class TaskAssetLinker : ITaskAssetLinker
{
    private static readonly Regex DescriptionImageRefPattern =
        new(@"tasks/files/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);

    private readonly IEntityAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public TaskAssetLinker(IEntityAssetRepository assets, IFileStorageService fileStorage, IUnitOfWork unitOfWork)
    {
        _assets = assets;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public Task SyncAttachmentsAsync(
        Guid tenantId, Guid userId, Guid taskId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default)
        => SyncAsync(tenantId, userId, EntityAssetOwnerTypes.Task, taskId, UploadPurposeCatalog.TaskAttachment, desiredFileIds, ct);

    public Task SyncDescriptionImagesAsync(
        Guid tenantId, Guid userId, Guid taskId, string? descriptionHtml, CancellationToken ct = default)
    {
        var desiredFileIds = ExtractFileIds(descriptionHtml);
        return SyncAsync(tenantId, userId, EntityAssetOwnerTypes.Task, taskId, UploadPurposeCatalog.TaskDescriptionImage, desiredFileIds, ct);
    }

    public Task SyncCommentAttachmentsAsync(
        Guid tenantId, Guid userId, Guid commentId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default)
        => SyncAsync(tenantId, userId, EntityAssetOwnerTypes.Comment, commentId, UploadPurposeCatalog.CommentAttachment, desiredFileIds, ct);

    public Task SyncCommentDescriptionImagesAsync(
        Guid tenantId, Guid userId, Guid commentId, string? contentHtml, CancellationToken ct = default)
    {
        var desiredFileIds = ExtractFileIds(contentHtml);
        return SyncAsync(tenantId, userId, EntityAssetOwnerTypes.Comment, commentId, UploadPurposeCatalog.CommentDescriptionImage, desiredFileIds, ct);
    }

    private static IReadOnlyList<Guid> ExtractFileIds(string? html)
        => string.IsNullOrEmpty(html)
            ? Array.Empty<Guid>()
            : DescriptionImageRefPattern.Matches(html)
                .Select(m => Guid.TryParse(m.Groups[1].Value, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();

    private async Task SyncAsync(
        Guid tenantId, Guid userId, string ownerType, Guid ownerId, string purpose, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct)
    {
        var current = (await _assets.ListByOwnerAsync(tenantId, ownerType, ownerId, ct))
            .Where(a => a.AssetPurpose == purpose)
            .ToList();
        var currentFileIds = current.Select(a => a.FileRecordId).ToHashSet();

        foreach (var fileId in desiredFileIds.Distinct())
        {
            if (currentFileIds.Contains(fileId))
                continue;

            var recordResult = await _fileStorage.GetRecordAsync(tenantId, fileId, ct);
            if (!recordResult.IsSuccess || recordResult.Value!.DeletedAt is not null || recordResult.Value.UploadedByUserId != userId)
                continue;

            var existingLink = await _assets.GetByFileRecordIdAsync(tenantId, fileId, ct);
            if (existingLink is not null)
                continue;

            await _assets.AddAsync(new EntityAsset
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OwnerType = ownerType,
                OwnerId = ownerId,
                AssetPurpose = purpose,
                FileRecordId = fileId,
                IsPrimary = false,
                CreatedByType = "user",
                CreatedById = userId,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        var desiredSet = desiredFileIds.ToHashSet();
        foreach (var asset in current.Where(a => !desiredSet.Contains(a.FileRecordId)))
        {
            var tracked = await _assets.GetByIdForTenantAsync(tenantId, asset.Id, ct);
            if (tracked is null)
                continue;

            await _assets.DeleteAsync(tracked, ct);
            await _fileStorage.DeleteAsync(tenantId, userId, asset.FileRecordId, ct);
        }

        // IEntityAssetRepository.AddAsync/DeleteAsync only stage changes on the tracked
        // DbContext - nothing else in this flow is guaranteed to flush them (the caller's own
        // transaction only commits already-saved changes). Save explicitly so a newly linked or
        // unlinked asset is actually persisted, proven missing by a real integration test: it
        // silently no-op'd under Moq-based unit tests since AddAsync there has no tracker to omit.
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
