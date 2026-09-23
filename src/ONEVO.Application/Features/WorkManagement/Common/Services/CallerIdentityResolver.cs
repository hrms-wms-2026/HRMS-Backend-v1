using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;

namespace ONEVO.Application.Features.WorkManagement.Common.Services;

public class CallerIdentityResolver : ICallerIdentityResolver
{
    private readonly IEmployeeRepository _employees;
    private readonly IEntityAssetRepository _entityAssets;

    public CallerIdentityResolver(IEmployeeRepository employees, IEntityAssetRepository entityAssets)
    {
        _employees = employees;
        _entityAssets = entityAssets;
    }

    public async Task<Guid?> ResolveCallerEmployeeIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByUserIdAsync(tenantId, userId, ct);
        return employee?.Id;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ResolveDisplayNamesByEmployeeIdAsync(
        Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var employeeId in employeeIds.Distinct())
        {
            var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is not null)
                result[employeeId] = $"{employee.FirstName} {employee.LastName}";
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, EmployeeIdentityDto>> ResolveIdentitiesByEmployeeIdAsync(
        Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default)
    {
        var distinctIds = employeeIds.Distinct().ToList();
        var avatarFileIdByEmployeeId = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.Employee, distinctIds, UploadPurposeCatalog.EmployeeAvatar, ct);

        var result = new Dictionary<Guid, EmployeeIdentityDto>();
        foreach (var employeeId in distinctIds)
        {
            var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is not null)
            {
                var avatarFileId = avatarFileIdByEmployeeId.TryGetValue(employeeId, out var fid) ? (Guid?)fid : null;
                result[employeeId] = new EmployeeIdentityDto($"{employee.FirstName} {employee.LastName}", avatarFileId);
            }
        }
        return result;
    }
}
