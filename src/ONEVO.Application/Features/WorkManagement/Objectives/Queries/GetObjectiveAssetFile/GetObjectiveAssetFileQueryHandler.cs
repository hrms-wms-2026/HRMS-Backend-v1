using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveAssetFile;

/// <summary>Streams a Module attachment to anyone who can read the Module, so a file is never more
/// visible than the Module it is attached to.</summary>
public sealed class GetObjectiveAssetFileQueryHandler : IRequestHandler<GetObjectiveAssetFileQuery, Result<ObjectiveAssetFile>>
{
    private readonly ObjectiveAssetAccess _access;
    private readonly IEntityAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;

    public GetObjectiveAssetFileQueryHandler(ObjectiveAssetAccess access, IEntityAssetRepository assets, IFileStorageService fileStorage)
    {
        _access = access;
        _assets = assets;
        _fileStorage = fileStorage;
    }

    public async Task<Result<ObjectiveAssetFile>> Handle(GetObjectiveAssetFileQuery request, CancellationToken ct)
    {
        var caller = await _access.ResolveAsync(request.ObjectiveId, ct);
        if (!caller.IsSuccess)
            return Result<ObjectiveAssetFile>.Failure(caller.Error!, caller.StatusCode ?? 400);

        var tenantId = caller.Value!.TenantId;
        var asset = await _assets.GetByIdForTenantAsync(tenantId, request.AssetId, ct);
        if (asset is null || asset.OwnerType != EntityAssetOwnerTypes.Objective || asset.OwnerId != request.ObjectiveId)
            return Result<ObjectiveAssetFile>.NotFound("Attachment not found.");

        var record = await _fileStorage.GetRecordAsync(tenantId, asset.FileRecordId, ct);
        if (!record.IsSuccess || record.Value!.DeletedAt is not null)
            return Result<ObjectiveAssetFile>.NotFound("Attachment not found.");

        var stream = await _fileStorage.OpenReadAsync(tenantId, record.Value, ct);
        if (!stream.IsSuccess)
            return Result<ObjectiveAssetFile>.Failure(stream.Error!, stream.StatusCode ?? 400);

        return Result<ObjectiveAssetFile>.Success(
            new ObjectiveAssetFile(stream.Value!.Content, stream.Value.ContentType, record.Value.OriginalFileName));
    }
}
