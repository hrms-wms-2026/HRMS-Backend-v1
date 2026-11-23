using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.Leave.Calendar.Services;

public sealed class LeaveVisibilityScopeProvider(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IEmployeeVisibilityScopeResolver visibilityScopes) : ILeaveVisibilityScopeProvider
{
    public async Task<LeaveVisibilityScopeResolution> ResolveForCurrentUserAsync(CancellationToken ct = default)
    {
        if (currentUser.HasPermission(PermissionCatalog.Leave.Manage) || currentUser.HasPermission(PermissionCatalog.Leave.Read))
            return new LeaveVisibilityScopeResolution(EmployeeVisibilityScope.Unrestricted(), null);

        if (currentUser.HasPermission(PermissionCatalog.Leave.ReadTeam))
        {
            return new LeaveVisibilityScopeResolution(
                await visibilityScopes.ResolveAsync(currentUser.TenantId, currentUser.UserId, ct), null);
        }

        if (currentUser.HasPermission(PermissionCatalog.Leave.ReadOwn))
        {
            var employee = await employees.GetByUserIdAsync(currentUser.TenantId, currentUser.UserId, ct);
            if (employee is null)
                return new LeaveVisibilityScopeResolution(null, LeaveVisibilityScopeFailure.NoEmployee);

            return new LeaveVisibilityScopeResolution(
                new EmployeeVisibilityScope(false, employee.Id, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()),
                null);
        }

        return new LeaveVisibilityScopeResolution(null, LeaveVisibilityScopeFailure.NoLeaveReadPermission);
    }
}
