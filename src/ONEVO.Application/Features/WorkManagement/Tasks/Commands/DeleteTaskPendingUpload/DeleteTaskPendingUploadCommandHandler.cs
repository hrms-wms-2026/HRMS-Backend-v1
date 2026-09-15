using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskPendingUpload;

public sealed class DeleteTaskPendingUploadCommandHandler : IRequestHandler<DeleteTaskPendingUploadCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IFileRecordRepository _fileRecords;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IFileStorageService _fileStorage;

    public DeleteTaskPendingUploadCommandHandler(
        ICurrentUser currentUser, IFileRecordRepository fileRecords, IEntityAssetRepository entityAssets, IFileStorageService fileStorage)
    {
        _currentUser = currentUser;
        _fileRecords = fileRecords;
        _entityAssets = entityAssets;
        _fileStorage = fileStorage;
    }

    public async Task<Result> Handle(DeleteTaskPendingUploadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var record = await _fileRecords.GetByIdAsync(tenantId, request.FileId, ct);
        if (record is null)
            return Result.NotFound("File not found.");

        if (record.UploadedByUserId != userId)
            return Result.Forbidden("You did not upload this file.");

        var existingLink = await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);
        if (existingLink is not null)
            return Result.Conflict("This file is already attached and cannot be deleted as a pending upload.");

        return await _fileStorage.DeleteAsync(tenantId, userId, request.FileId, ct);
    }
}
