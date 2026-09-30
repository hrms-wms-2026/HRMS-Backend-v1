using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

/// <summary>
/// Read-side access check for one employee, shared by every per-widget employee endpoint:
/// 404 when the employee is not in the tenant, 403 when outside the caller's visibility scope
/// (org:manage = unrestricted), otherwise the visible list-item row (name, position, ...).
/// GetEmployeeDetail/GetEmployeePositionHistory inline the same checks and are intentionally
/// left untouched.
/// </summary>
public interface IEmployeeReadAccessGuard
{
    Task<Result<EmployeeListItemResponse>> EnsureCanRead(Guid tenantId, Guid employeeId, CancellationToken ct = default);
}
