using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.EntityAssets.Services;

/// <summary>
/// Coverage-free by design, same reasoning as ICallerIdentityResolver's name/avatar resolution:
/// an avatar carries no more sensitivity than a display name, and every authenticated tenant
/// user already sees employee names across Work Management regardless of People-module
/// management-coverage scoping.
/// </summary>
public sealed class EmployeeEntityAssetAccessPolicy : IEntityAssetAccessPolicy
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;

    public EmployeeEntityAssetAccessPolicy(Common.RepositoryInterfaces.IEmployeeRepository employees) => _employees = employees;

    public async Task<bool> CanReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByIdAsync(tenantId, ownerId, ct);
        return employee is not null;
    }
}
