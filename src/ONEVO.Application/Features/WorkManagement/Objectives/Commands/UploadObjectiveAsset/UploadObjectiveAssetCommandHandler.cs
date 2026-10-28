using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset;

/// <summary>Uploads one file and attaches it to a Module. Size and type limits come from the
/// objective_asset upload purpose (25 MB; documents, images and ZIP files).</summary>
public sealed class UploadObjectiveAssetCommandHandler : IRequestHandler<UploadObjectiveAssetCommand, Result<ObjectiveAssetResponse>>
{
    private readonly ObjectiveAssetAccess _access;
    private readonly IEntityAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public UploadObjectiveAssetCommandHandler(
        ObjectiveAssetAccess access, IEntityAssetRepository assets, IFileStorageService fileStorage, IUnitOfWork unitOfWork)
    {
        _access = access;
        _assets = assets;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveAssetResponse>> Handle(UploadObjectiveAssetCommand request, CancellationToken ct)
    {
        var caller = await _access.ResolveAsync(request.ObjectiveId, ct);
        if (!caller.IsSuccess)
            return Result<ObjectiveAssetResponse>.Failure(caller.Error!, caller.StatusCode ?? 400);

        var (tenantId, userId, _, objective, _) = caller.Value!;
        var upload = await _fileStorage.UploadAsync(
            tenantId, userId, request.OriginalFileName, request.ContentType, UploadPurposeCatalog.ObjectiveAsset, request.Content, ct);
        if (!upload.IsSuccess)
            return Result<ObjectiveAssetResponse>.Failure(upload.Error!, upload.StatusCode ?? 400);

        var file = upload.Value!;
        var asset = new EntityAsset
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerType = EntityAssetOwnerTypes.Objective,
            OwnerId = objective.Id,
            AssetPurpose = UploadPurposeCatalog.ObjectiveAsset,
            FileRecordId = file.Id,
            IsPrimary = false,
            CreatedByType = "user",
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _assets.AddAsync(asset, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveAssetResponse>.Success(new ObjectiveAssetResponse(
            asset.Id, file.OriginalFileName, file.FileSizeBytes, file.ContentType, file.CreatedAt,
            ObjectiveAssetResponse.DownloadPath(objective.Id, asset.Id)));
    }
}
