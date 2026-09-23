using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.File.Queries.GetFile;

/// <summary>
/// Resolves a pending file for its uploader or a linked file through the registered
/// owner-type access policy. Unknown owner types fail closed.
/// </summary>
public sealed class GetFileQueryHandler : IRequestHandler<GetFileQuery, Result<FileStreamDto>>
{
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IEntityAssetAccessPolicyResolver _policyResolver;
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public GetFileQueryHandler(
        IEntityAssetRepository entityAssets,
        IEntityAssetAccessPolicyResolver policyResolver,
        IFileStorageService fileStorage,
        ICurrentUser currentUser)
    {
        _entityAssets = entityAssets;
        _policyResolver = policyResolver;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<FileStreamDto>> Handle(GetFileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var link = await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);
        if (link is null)
        {
            var recordResult = await _fileStorage.GetRecordAsync(tenantId, request.FileId, ct);
            if (!recordResult.IsSuccess ||
                recordResult.Value!.DeletedAt is not null ||
                recordResult.Value.UploadedByUserId != _currentUser.UserId)
            {
                return Result<FileStreamDto>.NotFound("File not found.");
            }

            return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
        }

        var policy = _policyResolver.Resolve(link.OwnerType);
        if (policy is null || !await policy.CanReadAsync(tenantId, link.OwnerId, ct))
            return Result<FileStreamDto>.NotFound("File not found.");

        return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
    }
}
