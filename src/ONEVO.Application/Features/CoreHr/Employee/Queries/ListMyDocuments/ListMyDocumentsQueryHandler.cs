using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Documents;
using ONEVO.Application.Features.Storage.File.Helpers;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.ListMyDocuments;

public sealed class ListMyDocumentsQueryHandler
    : IRequestHandler<ListMyDocumentsQuery, Result<IReadOnlyList<EmployeeDocumentResponse>>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;
    private readonly IEntityAssetRepository _assets;
    private readonly ICurrentUser _currentUser;

    public ListMyDocumentsQueryHandler(
        Common.RepositoryInterfaces.IEmployeeRepository employees,
        IEntityAssetRepository assets,
        ICurrentUser currentUser)
    {
        _employees = employees;
        _assets = assets;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<EmployeeDocumentResponse>>> Handle(ListMyDocumentsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<EmployeeDocumentResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _employees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<IReadOnlyList<EmployeeDocumentResponse>>.NotFound("No employee record for the current user.");

        var assets = await _assets.ListWithMetadataByOwnerAsync(tenantId, EntityAssetOwnerTypes.EmployeeDocument, employee.Id, ct);
        IReadOnlyList<EmployeeDocumentResponse> documents = assets
            .Where(a => a.AssetPurpose == UploadPurposeCatalog.EmployeeDocument)
            .Select(EmployeeDocumentResponse.From)
            .ToList();
        return Result<IReadOnlyList<EmployeeDocumentResponse>>.Success(documents);
    }
}
