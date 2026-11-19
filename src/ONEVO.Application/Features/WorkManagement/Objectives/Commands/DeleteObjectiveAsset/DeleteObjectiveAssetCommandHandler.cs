using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjectiveAsset;

/// <summary>Removes a Module attachment and its stored file. Allowed for whoever uploaded it, the
/// Module's head, or a full admin; any other member gets 403.</summary>
public sealed class DeleteObjectiveAssetCommandHandler : IRequestHandler<DeleteObjectiveAssetCommand, Result>
{
    private readonly ObjectiveAssetAccess _access;
    private readonly IEntityAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteObjectiveAssetCommandHandler(
        ObjectiveAssetAccess access, IEntityAssetRepository assets, IFileStorageService fileStorage, IUnitOfWork unitOfWork)
    {
        _access = access;
        _assets = assets;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteObjectiveAssetCommand request, CancellationToken ct)
    {
        var caller = await _access.ResolveAsync(request.ObjectiveId, ct);
        if (!caller.IsSuccess)
            return Result.Failure(caller.Error!, caller.StatusCode ?? 400);

        var (tenantId, userId, _, objective, _) = caller.Value!;
        var asset = await _assets.GetByIdForTenantAsync(tenantId, request.AssetId, ct);
        if (asset is null || asset.OwnerType != EntityAssetOwnerTypes.Objective || asset.OwnerId != objective.Id)
            return Result.NotFound("Attachment not found.");

        if (!caller.Value!.CanRemove(asset.CreatedById))
            return Result.Forbidden("Only the person who attached this file or the module head can remove it.");

        var fileId = asset.FileRecordId;
        await _assets.DeleteAsync(asset, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _fileStorage.DeleteAsync(tenantId, userId, fileId, ct);
        return Result.Success();
    }
}
