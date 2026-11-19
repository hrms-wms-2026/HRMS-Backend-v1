using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

/// <summary>
/// Write-side visibility check for a single employee: the caller may change an employee only if
/// that employee is inside the caller's visibility scope (org:manage = unrestricted) - the same
/// resolution the employee detail screen uses to decide what the caller can see.
/// </summary>
public interface IEmployeeManageScopeGuard
{
    /// <returns>null when allowed; a 403 Result otherwise.</returns>
    Task<Result?> EnsureCanManage(Guid tenantId, Guid employeeId, CancellationToken ct = default);
}
