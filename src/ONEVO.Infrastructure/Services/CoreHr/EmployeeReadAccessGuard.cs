using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Infrastructure.Services.CoreHr;

public sealed class EmployeeReadAccessGuard(
    ICurrentUser currentUser,
    IEmployeeVisibilityScopeResolver visibilityScopeResolver,
    IEmployeeRepository employeeRepository) : IEmployeeReadAccessGuard
{
    public const string NotFoundMessage = "The employee or selected organization record could not be found.";
    public const string ForbiddenMessage = "You do not have access to view this employee.";

    public async Task<Result<EmployeeListItemResponse>> EnsureCanRead(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var existing = await employeeRepository.GetByIdAsync(tenantId, employeeId, ct);
        if (existing is null)
            return Result<EmployeeListItemResponse>.NotFound(NotFoundMessage);

        var scope = currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await visibilityScopeResolver.ResolveAsync(tenantId, currentUser.UserId, ct);

        var visible = await employeeRepository.GetVisibleByIdAsync(tenantId, scope, employeeId, ct);
        return visible is null
            ? Result<EmployeeListItemResponse>.Forbidden(ForbiddenMessage)
            : Result<EmployeeListItemResponse>.Success(visible);
    }
}
