using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.File.Commands.DeleteFile;

public sealed class DeleteFileCommandHandler : IRequestHandler<DeleteFileCommand, Result>
{
    private readonly IFileStorageService _fileStorage;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ICurrentUser _currentUser;

    public DeleteFileCommandHandler(
        IFileStorageService fileStorage,
        IEntityAssetRepository entityAssets,
        ICurrentUser currentUser)
    {
        _fileStorage = fileStorage;
        _entityAssets = entityAssets;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeleteFileCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct) is not null)
            return Result.Conflict("A linked file cannot be deleted through the pending-file endpoint.");

        var link = await _fileStorage.GetRecordAsync(tenantId, request.FileId, ct);
        if (!link.IsSuccess)
            return Result.Failure(link.Error!, link.StatusCode ?? 400);

        if (link.Value!.UploadedByUserId != _currentUser.UserId)
            return Result.Forbidden("Only the uploader may delete this file.");

        return await _fileStorage.DeleteAsync(tenantId, _currentUser.UserId, request.FileId, ct);
    }
}
