using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Documents;

public interface IEmployeeDocumentAccess
{
    /// <summary>True when the caller may view (list/open) this employee's personal documents.</summary>
    Task<bool> CanViewAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
}

/// <summary>
/// The owning employee always sees their own documents. Anyone else needs the employee inside
/// their management coverage (org:manage is unrestricted) - the same relationship rule as
/// GetEmployeeDetail, no permission code. View-only: upload/delete stay self-service.
/// </summary>
public sealed class EmployeeDocumentAccess : IEmployeeDocumentAccess
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;
    private readonly RepositoryInterfaces.IEmployeeRepository _visibleEmployees;
    private readonly IEmployeeVisibilityScopeResolver _scopeResolver;
    private readonly ICurrentUser _currentUser;

    public EmployeeDocumentAccess(
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

    public async Task<bool> CanViewAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
            return false;

        var self = await _employees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (self is not null && self.Id == employeeId)
            return true;

        var scope = _currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await _scopeResolver.ResolveAsync(tenantId, _currentUser.UserId, ct);
        return await _visibleEmployees.GetVisibleByIdAsync(tenantId, scope, employeeId, ct) is not null;
    }
}
