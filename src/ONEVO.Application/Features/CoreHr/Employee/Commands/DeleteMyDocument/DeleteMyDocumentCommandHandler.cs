using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteMyDocument;

public sealed class DeleteMyDocumentCommandHandler : IRequestHandler<DeleteMyDocumentCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;
    private readonly IEntityAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public DeleteMyDocumentCommandHandler(
        Common.RepositoryInterfaces.IEmployeeRepository employees,
        IEntityAssetRepository assets,
        IFileStorageService fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser)
    {
        _employees = employees;
        _assets = assets;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeleteMyDocumentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _employees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("No employee record for the current user.");

        var asset = await _assets.GetByIdForTenantAsync(tenantId, request.DocumentId, ct);
        if (asset is null ||
            asset.OwnerType != EntityAssetOwnerTypes.EmployeeDocument ||
            asset.OwnerId != employee.Id)
        {
            return Result.NotFound("Document not found.");
        }

        var fileId = asset.FileRecordId;
        await _assets.DeleteAsync(asset, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _fileStorage.DeleteAsync(tenantId, _currentUser.UserId, fileId, ct);
        return Result.Success();
    }
}
