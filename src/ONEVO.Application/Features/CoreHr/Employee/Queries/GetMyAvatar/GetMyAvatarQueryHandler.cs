using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyAvatar;

public class GetMyAvatarQueryHandler : IRequestHandler<GetMyAvatarQuery, Result<FileStreamDto>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public GetMyAvatarQueryHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IFileStorageService fileStorage,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<FileStreamDto>> Handle(GetMyAvatarQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null || employee.AvatarFileId is null)
            return Result<FileStreamDto>.NotFound("Avatar not found.");

        return await _fileStorage.OpenReadAsync(tenantId, employee.AvatarFileId.Value, ct);
    }
}
