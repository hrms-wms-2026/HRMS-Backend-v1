using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.Leave.Request.Commands.UploadLeaveDocument;

public sealed class UploadLeaveDocumentCommandHandler
    : IRequestHandler<UploadLeaveDocumentCommand, Result<FileRecordDto>>
{
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public UploadLeaveDocumentCommandHandler(IFileStorageService fileStorage, ICurrentUser currentUser)
    {
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<FileRecordDto>> Handle(UploadLeaveDocumentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileRecordDto>.Forbidden("Authentication required.");

        if (!_currentUser.HasPermission("leave:read-own") && !_currentUser.HasPermission("leave:manage"))
            return Result<FileRecordDto>.Forbidden("Permission 'leave:read-own' or 'leave:manage' required.");

        return await _fileStorage.UploadAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            request.FileName,
            request.ContentType,
            UploadPurposeCatalog.LeaveSupportingDocument,
            request.Content,
            ct);
    }
}
