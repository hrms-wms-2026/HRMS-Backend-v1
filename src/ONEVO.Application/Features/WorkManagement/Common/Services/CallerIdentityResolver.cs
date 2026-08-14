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
}
