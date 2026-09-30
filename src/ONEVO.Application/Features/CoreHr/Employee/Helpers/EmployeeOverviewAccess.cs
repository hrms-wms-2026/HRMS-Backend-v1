using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>
/// Per-widget module gate for the employee Overview: the caller passes if they hold the module
/// permission, or if they are looking at their own employee record (self-service needs no
/// extra permission). Coverage/visibility is checked separately by IEmployeeReadAccessGuard.
/// </summary>
public static class EmployeeOverviewAccess
{
    public static async Task<bool> HasAccessAsync(
        ICurrentUser user,
        IEmployeeRepository employees,
        Guid tenantId,
        Guid employeeId,
        string permission,
        CancellationToken ct)
    {
        if (user.HasPermission(permission))
            return true;

        var caller = await employees.GetDefaultForUserAsync(tenantId, user.UserId, ct);
        return caller is not null && caller.Id == employeeId;
    }
}
