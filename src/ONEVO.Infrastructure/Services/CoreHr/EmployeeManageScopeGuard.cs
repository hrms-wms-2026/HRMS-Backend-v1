using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Infrastructure.Services.CoreHr;

public sealed class EmployeeManageScopeGuard(
    ICurrentUser currentUser,
    IEmployeeVisibilityScopeResolver visibilityScopeResolver,
    IEmployeeRepository employeeRepository) : IEmployeeManageScopeGuard
{
    public const string ForbiddenMessage = "You do not have access to manage this employee.";

    public async Task<Result?> EnsureCanManage(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var scope = currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await visibilityScopeResolver.ResolveAsync(tenantId, currentUser.UserId, ct);

        var visible = await employeeRepository.GetVisibleByIdAsync(tenantId, scope, employeeId, ct);
        return visible is null ? Result.Forbidden(ForbiddenMessage) : null;
    }
}
