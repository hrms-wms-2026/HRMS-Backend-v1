using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload;

public sealed class CreateTaskPendingUploadCommandHandler : IRequestHandler<CreateTaskPendingUploadCommand, Result<FileRecordDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorageService _fileStorage;

    public CreateTaskPendingUploadCommandHandler(ICurrentUser currentUser, IFileStorageService fileStorage)
    {
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileRecordDto>> Handle(CreateTaskPendingUploadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileRecordDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<FileRecordDto>.Forbidden("Tenant context missing.");

        if (request.Purpose != UploadPurposeCatalog.TaskAttachment && request.Purpose != UploadPurposeCatalog.TaskDescriptionImage)
            return Result<FileRecordDto>.Failure("Unsupported upload purpose for a task file.", 400);

        return await _fileStorage.UploadAsync(
            tenantId, _currentUser.UserId, request.OriginalFileName, request.ContentType, request.Purpose, request.Content, ct);
    }
}
