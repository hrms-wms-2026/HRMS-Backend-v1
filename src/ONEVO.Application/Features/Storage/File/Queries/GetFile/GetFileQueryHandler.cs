using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.File.Queries.GetFile;

/// <summary>
/// Resolves a pending file for its uploader or a linked file through the registered
/// owner-type access policy. Unknown owner types fail closed.
/// </summary>
public sealed class GetFileQueryHandler : IRequestHandler<GetFileQuery, Result<FileDownloadDto>>
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

    public async Task<Result<FileDownloadDto>> Handle(GetFileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileDownloadDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var link = await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);
        FileRecordDto fileRecord;

        if (link is null)
        {
            var recordResult = await _fileStorage.GetRecordAsync(tenantId, request.FileId, ct);
            if (!recordResult.IsSuccess ||
                recordResult.Value!.DeletedAt is not null ||
                recordResult.Value.UploadedByUserId != _currentUser.UserId)
            {
                return Result<FileDownloadDto>.NotFound("File not found.");
            }

            fileRecord = recordResult.Value;
        }
        else
        {
            var policy = _policyResolver.Resolve(link.OwnerType);
            if (policy is null || !await policy.CanReadAsync(tenantId, link.OwnerId, ct))
                return Result<FileDownloadDto>.NotFound("File not found.");

            var recordResult = await _fileStorage.GetRecordAsync(tenantId, request.FileId, ct);
            if (!recordResult.IsSuccess || recordResult.Value!.DeletedAt is not null)
                return Result<FileDownloadDto>.NotFound("File not found.");

            fileRecord = recordResult.Value;
        }

        var isCacheableAvatar = link?.AssetPurpose.Equals(
            UploadPurposeCatalog.EmployeeAvatar, StringComparison.Ordinal) == true;
        var etag = isCacheableAvatar
            ? $"\"sha256-{fileRecord.ChecksumSha256}\""
            : null;

        if (etag is not null && MatchesIfNoneMatch(request.IfNoneMatch, etag))
        {
            return Result<FileDownloadDto>.Success(new FileDownloadDto(
                null,
                fileRecord.ContentType,
                fileRecord.FileSizeBytes,
                etag,
                true,
                true));
        }

        var streamResult = await _fileStorage.OpenReadAsync(tenantId, fileRecord, ct);
        if (!streamResult.IsSuccess)
        {
            return Result<FileDownloadDto>.Failure(
                streamResult.Error!, streamResult.StatusCode ?? 500, streamResult.ErrorCode);
        }

        return Result<FileDownloadDto>.Success(new FileDownloadDto(
            streamResult.Value!.Content,
            streamResult.Value.ContentType,
            fileRecord.FileSizeBytes,
            etag,
            isCacheableAvatar,
            false));
    }

    private static bool MatchesIfNoneMatch(string? ifNoneMatch, string currentEtag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
            return false;

        foreach (var rawValue in ifNoneMatch.Split(','))
        {
            var candidate = rawValue.Trim();
            if (candidate == "*")
                return true;

            if (candidate.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                candidate = candidate[2..].TrimStart();

            if (string.Equals(candidate, currentEtag, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
