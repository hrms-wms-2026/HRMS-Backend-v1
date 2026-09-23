using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Features.Storage.EntityAssets.Services;

public sealed class PrimaryEntityAssetLinker : IPrimaryEntityAssetLinker
{
    private readonly IEntityAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public PrimaryEntityAssetLinker(
        IEntityAssetRepository assets,
        IFileStorageService fileStorage,
        IUnitOfWork unitOfWork)
    {
        _assets = assets;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid?>> LinkAsync(
        Guid tenantId,
        Guid userId,
        string ownerType,
        Guid ownerId,
        string purpose,
        Guid fileId,
        CancellationToken ct = default)
    {
        var rule = UploadPurposeCatalog.GetRule(purpose);
        if (rule is null)
            return Result<Guid?>.Failure($"Unsupported upload purpose '{purpose}'.", 400);

        var recordResult = await _fileStorage.GetRecordAsync(tenantId, fileId, ct);
        if (!recordResult.IsSuccess ||
            recordResult.Value!.DeletedAt is not null ||
            recordResult.Value.UploadedByUserId != userId)
        {
            return Result<Guid?>.NotFound("File not found.");
        }

        var record = recordResult.Value;
        var extension = Path.GetExtension(record.OriginalFileName);
        if (record.FileSizeBytes > rule.MaxSizeBytes ||
            !rule.AllowedContentTypes.Contains(record.ContentType, StringComparer.OrdinalIgnoreCase) ||
            !rule.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Result<Guid?>.Failure("The uploaded file is not valid for this asset purpose.", 400);
        }

        var existingLink = await _assets.GetByFileRecordIdAsync(tenantId, fileId, ct);
        if (existingLink is not null)
        {
            return existingLink.OwnerType == ownerType &&
                   existingLink.OwnerId == ownerId &&
                   existingLink.AssetPurpose == purpose &&
                   existingLink.IsPrimary
                ? Result<Guid?>.Success(fileId)
                : Result<Guid?>.Conflict("The file is already linked to another owner.");
        }

        var current = (await _assets.ListByOwnerAsync(tenantId, ownerType, ownerId, ct))
            .Where(a => a.AssetPurpose == purpose)
            .ToList();
        foreach (var item in current)
        {
            var tracked = await _assets.GetByIdForTenantAsync(tenantId, item.Id, ct);
            if (tracked is not null)
                await _assets.DeleteAsync(tracked, ct);
        }

        await _assets.AddAsync(new EntityAsset
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            AssetPurpose = purpose,
            FileRecordId = fileId,
            IsPrimary = true,
            CreatedByType = "user",
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        foreach (var oldFileId in current.Select(a => a.FileRecordId).Where(id => id != fileId).Distinct())
            await _fileStorage.DeleteAsync(tenantId, userId, oldFileId, ct);

        return Result<Guid?>.Success(fileId);
    }

    public async Task<Result> RemoveAsync(
        Guid tenantId,
        Guid userId,
        string ownerType,
        Guid ownerId,
        string purpose,
        CancellationToken ct = default)
    {
        var current = (await _assets.ListByOwnerAsync(tenantId, ownerType, ownerId, ct))
            .Where(a => a.AssetPurpose == purpose)
            .ToList();
        foreach (var item in current)
        {
            var tracked = await _assets.GetByIdForTenantAsync(tenantId, item.Id, ct);
            if (tracked is not null)
                await _assets.DeleteAsync(tracked, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        foreach (var fileId in current.Select(a => a.FileRecordId).Distinct())
            await _fileStorage.DeleteAsync(tenantId, userId, fileId, ct);

        return Result.Success();
    }
}
