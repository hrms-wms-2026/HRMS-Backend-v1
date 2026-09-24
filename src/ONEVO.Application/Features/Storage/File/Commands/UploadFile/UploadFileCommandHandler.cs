using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.File.Commands.UploadFile;

public sealed class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, Result<FileRecordDto>>
{
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public UploadFileCommandHandler(IFileStorageService fileStorage, ICurrentUser currentUser)
    {
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<FileRecordDto>> Handle(UploadFileCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileRecordDto>.Forbidden("Authentication required.");

        if (!UploadPurposeCatalog.IsSupported(request.Purpose))
            return Result<FileRecordDto>.Failure($"Unsupported upload purpose '{request.Purpose}'.", 400);

        return await _fileStorage.UploadAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            request.FileName,
            request.ContentType,
            request.Purpose,
            request.Content,
            ct);
    }
}
