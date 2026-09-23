using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Infrastructure.Services.CoreHr.Offboarding;

public sealed class EmployeeOffboardingCoverageGuard(
    IEmployeeVisibilityScopeResolver scopeResolver,
    IEmployeeRepository employeeRepository,
    IPositionAssignmentRepository positionAssignmentRepository)
    : IEmployeeOffboardingCoverageGuard
{
    public async Task<Result?> EnsureCovered(Guid tenantId, Guid actingUserId, Guid targetEmployeeId, CancellationToken ct = default)
    {
        var employee = await employeeRepository.GetByIdAsync(tenantId, targetEmployeeId, ct);
        if (employee is null)
            return null; // Not this guard's concern - the caller's own not-found check handles it.

        // Deliberately never substitutes EmployeeVisibilityScope.Unrestricted() - every caller,
        // including org:manage-style admins, is scoped by literal coverage rows here (design spec §11).
        var scope = await scopeResolver.ResolveAsync(tenantId, actingUserId, ct);

        var activeAssignment = await positionAssignmentRepository.GetActivePrimaryAsync(tenantId, targetEmployeeId, ct);
        var isCovered =
            (activeAssignment is not null && scope.CoveredPositionIds.Contains(activeAssignment.PositionId))
            || (employee.DepartmentId is not null && scope.CoveredDepartmentIds.Contains(employee.DepartmentId.Value))
            || (employee.LegalEntityId is not null && scope.CompanyWideLegalEntityIds.Contains(employee.LegalEntityId.Value));

        return isCovered ? null : Result.Forbidden("You do not have management coverage over this employee.");
    }
}
