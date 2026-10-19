using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Documents;
using ONEVO.Application.Features.Storage.File.Helpers;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployeeDocuments;

public sealed class ListEmployeeDocumentsQueryHandler
    : IRequestHandler<ListEmployeeDocumentsQuery, Result<IReadOnlyList<EmployeeDocumentResponse>>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;
    private readonly IEmployeeDocumentAccess _access;
    private readonly IEntityAssetRepository _assets;
    private readonly ICurrentUser _currentUser;

    public ListEmployeeDocumentsQueryHandler(
        Common.RepositoryInterfaces.IEmployeeRepository employees,
        IEmployeeDocumentAccess access,
        IEntityAssetRepository assets,
        ICurrentUser currentUser)
    {
        _employees = employees;
        _access = access;
        _assets = assets;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<EmployeeDocumentResponse>>> Handle(ListEmployeeDocumentsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<EmployeeDocumentResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (await _employees.GetByIdAsync(tenantId, request.EmployeeId, ct) is null)
            return Result<IReadOnlyList<EmployeeDocumentResponse>>.NotFound("Employee not found.");

        if (!await _access.CanViewAsync(tenantId, request.EmployeeId, ct))
            return Result<IReadOnlyList<EmployeeDocumentResponse>>.Forbidden("You do not have access to this employee's documents.");

        var assets = await _assets.ListWithMetadataByOwnerAsync(tenantId, EntityAssetOwnerTypes.EmployeeDocument, request.EmployeeId, ct);
        IReadOnlyList<EmployeeDocumentResponse> documents = assets
            .Where(a => a.AssetPurpose == UploadPurposeCatalog.EmployeeDocument)
            .Select(EmployeeDocumentResponse.From)
            .ToList();
        return Result<IReadOnlyList<EmployeeDocumentResponse>>.Success(documents);
    }
}
