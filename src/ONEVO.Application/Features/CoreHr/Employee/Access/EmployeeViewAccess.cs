using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Access;

public interface IEmployeeViewAccess
{
    /// <summary>True when the caller may view this employee's records (profile widgets, history, checklists).</summary>
    Task<bool> CanViewAsync(Guid employeeId, CancellationToken ct = default);
}

/// <summary>
/// Relationship-based employee visibility, with no permission code: you can view yourself and the
/// employees inside your management coverage (management_coverage_records on your own position).
/// The same rule GetEmployee/GetEmployeeDetail apply; this lets per-employee endpoints whose
/// handlers never scoped by themselves enforce it in one place (RequireVisibleEmployeeAttribute).
/// </summary>
public sealed class EmployeeViewAccess : IEmployeeViewAccess
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;
    private readonly RepositoryInterfaces.IEmployeeRepository _visibleEmployees;
    private readonly IEmployeeVisibilityScopeResolver _scopeResolver;
    private readonly ICurrentUser _currentUser;

    public EmployeeViewAccess(
        Common.RepositoryInterfaces.IEmployeeRepository employees,
        RepositoryInterfaces.IEmployeeRepository visibleEmployees,
        IEmployeeVisibilityScopeResolver scopeResolver,
        ICurrentUser currentUser)
    {
        _employees = employees;
        _visibleEmployees = visibleEmployees;
        _scopeResolver = scopeResolver;
        _currentUser = currentUser;
    }

    public async Task<bool> CanViewAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
            return false;

        var tenantId = _currentUser.TenantId;
        var self = await _employees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (self is not null && self.Id == employeeId)
            return true;

        var scope = await _scopeResolver.ResolveAsync(tenantId, _currentUser.UserId, ct);
        return await _visibleEmployees.GetVisibleByIdAsync(tenantId, scope, employeeId, ct) is not null;
    }
}
