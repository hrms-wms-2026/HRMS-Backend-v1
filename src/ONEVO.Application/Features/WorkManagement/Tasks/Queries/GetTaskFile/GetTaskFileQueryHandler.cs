using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskFile;

/// <summary>
/// Mirrors GetTaskByIdQueryHandler's relationship-based access rule exactly
/// (active objective membership via ITaskAccessResolver) for a file already
/// linked to a task or a comment, so a task's attachment/inline image, or a
/// comment's, is never more visible than the task itself. A file that isn't
/// linked to anything yet (a "pending upload") is visible only to whoever
/// uploaded it.
/// </summary>
public sealed class GetTaskFileQueryHandler : IRequestHandler<GetTaskFileQuery, Result<FileStreamDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskAccessResolver _access;
    private readonly IFileStorageService _fileStorage;

    public GetTaskFileQueryHandler(
        ICurrentUser currentUser, IEntityAssetRepository entityAssets, ITaskCommentRepository comments,
        ITaskAccessResolver access, IFileStorageService fileStorage)
    {
        _currentUser = currentUser;
        _entityAssets = entityAssets;
        _comments = comments;
        _access = access;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileStreamDto>> Handle(GetTaskFileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<FileStreamDto>.Forbidden("Tenant context missing.");

        var link = await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);

        if (link is null)
        {
            var recordResult = await _fileStorage.GetRecordAsync(tenantId, request.FileId, ct);
            if (!recordResult.IsSuccess || recordResult.Value!.DeletedAt is not null || recordResult.Value.UploadedByUserId != userId)
                return Result<FileStreamDto>.NotFound("File not found.");

            return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
        }

        Guid taskId;
        if (link.OwnerType == EntityAssetOwnerTypes.Task)
        {
            taskId = link.OwnerId;
        }
        else if (link.OwnerType == EntityAssetOwnerTypes.Comment)
        {
            var comment = await _comments.GetByIdForTenantAsync(tenantId, link.OwnerId, ct);
            if (comment is null)
                return Result<FileStreamDto>.NotFound("File not found.");
            taskId = comment.TaskId;
        }
        else
        {
            return Result<FileStreamDto>.NotFound("File not found.");
        }

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, taskId, ct);
        if (!access.IsSuccess)
            return Result<FileStreamDto>.NotFound("File not found.");

        return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
    }
}
