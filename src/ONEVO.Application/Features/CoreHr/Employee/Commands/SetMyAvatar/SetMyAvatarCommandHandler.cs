using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.SetMyAvatar;

public class SetMyAvatarCommandHandler : IRequestHandler<SetMyAvatarCommand, Result<Guid?>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeRepository _featureEmployees;
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public SetMyAvatarCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IEmployeeRepository featureEmployees,
        IFileStorageService fileStorage,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _featureEmployees = featureEmployees;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid?>> Handle(SetMyAvatarCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid?>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var lookup = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (lookup is null)
            return Result<Guid?>.NotFound("No employee record for the current user.");

        var uploadResult = await _fileStorage.UploadAsync(
            tenantId, _currentUser.UserId, request.FileName, request.ContentType,
            UploadPurposeCatalog.EmployeeAvatar, request.Content, ct);

        if (!uploadResult.IsSuccess)
            return Result<Guid?>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

        var tracked = await _featureEmployees.GetTrackedByIdAsync(tenantId, lookup.Id, ct);
        if (tracked is null)
            return Result<Guid?>.NotFound("No employee record for the current user.");

        tracked.AvatarFileId = uploadResult.Value!.Id;
        tracked.UpdatedAt = DateTimeOffset.UtcNow;
        await _featureEmployees.SaveChangesAsync(ct);

        return Result<Guid?>.Success(tracked.AvatarFileId);
    }
}
