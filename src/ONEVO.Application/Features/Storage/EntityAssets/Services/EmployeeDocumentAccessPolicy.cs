using ONEVO.Application.Features.CoreHr.Employee.Documents;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.EntityAssets.Services;

/// <summary>
/// Personal documents (CV, national ID, certificates) are NOT coverage-free like
/// EmployeeEntityAssetAccessPolicy's avatars: readable by the owning employee, or by a caller with
/// employees:read who has the employee in their management coverage (see EmployeeDocumentAccess).
/// </summary>
public sealed class EmployeeDocumentAccessPolicy : IEntityAssetAccessPolicy
{
    private readonly IEmployeeDocumentAccess _access;

    public EmployeeDocumentAccessPolicy(IEmployeeDocumentAccess access) => _access = access;

    public Task<bool> CanReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct = default)
        => _access.CanViewAsync(tenantId, ownerId, ct);
}
