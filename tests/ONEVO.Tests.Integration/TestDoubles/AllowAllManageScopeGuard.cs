using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Tests.Integration.TestDoubles;

/// <summary>Scope is covered by EmployeeManageScopeGuardTests; integration tests of handler
/// behaviour run as an org:manage caller, for whom the real guard also allows everything.</summary>
internal sealed class AllowAllManageScopeGuard : IEmployeeManageScopeGuard
{
    public Task<Result?> EnsureCanManage(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => Task.FromResult<Result?>(null);
}
