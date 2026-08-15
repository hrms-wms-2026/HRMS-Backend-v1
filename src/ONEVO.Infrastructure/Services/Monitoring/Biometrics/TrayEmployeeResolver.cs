using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

public sealed class TrayEmployeeResolver : ITrayEmployeeResolver
{
    private readonly IEmployeeRepository _employees;

    public TrayEmployeeResolver(IEmployeeRepository employees) => _employees = employees;

    public async Task<EmployeeIdentity?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var employee = await _employees.GetByUserIdAsync(tenantId, userId, ct);
        if (employee is null)
            return null;

        return new EmployeeIdentity(
            employee.Id,
            employee.EmployeeNumber,
            $"{employee.FirstName} {employee.LastName}".Trim(),
            employee.Email);
    }
}
