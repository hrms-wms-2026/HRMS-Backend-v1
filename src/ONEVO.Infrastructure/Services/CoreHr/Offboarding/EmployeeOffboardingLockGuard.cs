using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Domain.Lookups;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Infrastructure.Services.CoreHr.Offboarding;

public sealed class EmployeeOffboardingLockGuard(IEmployeeRepository employeeRepository) : IEmployeeOffboardingLockGuard
{
    public async Task<Result?> EnsureMutable(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var employee = await employeeRepository.GetByIdAsync(tenantId, employeeId, ct);
        if (employee is null)
            return null; // Not this guard's concern - the caller's own not-found check handles it.

        if (employee.EmploymentStatusId is EmploymentStatusIds.Resigned or EmploymentStatusIds.Terminated)
            return Result.Conflict("This employee's record is read-only after offboarding completion.");

        return null;
    }
}
