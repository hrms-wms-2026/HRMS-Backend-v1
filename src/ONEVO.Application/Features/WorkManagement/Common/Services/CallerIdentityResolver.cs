using ONEVO.Application.Common.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Common.Services;

public class CallerIdentityResolver : ICallerIdentityResolver
{
    private readonly IEmployeeRepository _employees;

    public CallerIdentityResolver(IEmployeeRepository employees) => _employees = employees;

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
        var result = new Dictionary<Guid, EmployeeIdentityDto>();
        foreach (var employeeId in employeeIds.Distinct())
        {
            var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is not null)
                result[employeeId] = new EmployeeIdentityDto($"{employee.FirstName} {employee.LastName}", employee.AvatarFileId);
        }
        return result;
    }
}
