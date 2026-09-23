using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.LinkMyAvatar;

public sealed class LinkMyAvatarCommandHandler : IRequestHandler<LinkMyAvatarCommand, Result<Guid?>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;
    private readonly IPrimaryEntityAssetLinker _linker;
    private readonly ICurrentUser _currentUser;

    public LinkMyAvatarCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository employees,
        IPrimaryEntityAssetLinker linker,
        ICurrentUser currentUser)
    {
        _employees = employees;
        _linker = linker;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid?>> Handle(LinkMyAvatarCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<Guid?>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _employees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<Guid?>.NotFound("No employee record for the current user.");

        return await _linker.LinkAsync(
            tenantId, _currentUser.UserId, EntityAssetOwnerTypes.Employee, employee.Id,
            UploadPurposeCatalog.EmployeeAvatar, request.FileId, ct);
    }
}
