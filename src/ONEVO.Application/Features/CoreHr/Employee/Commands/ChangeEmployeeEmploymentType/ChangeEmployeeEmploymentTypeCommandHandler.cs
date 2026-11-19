using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeeEmploymentType;

public sealed class ChangeEmployeeEmploymentTypeCommandHandler(
    IEmployeeRepository employeeRepository,
    IEmployeeManageScopeGuard manageScopeGuard,
    IEmployeeOffboardingLockGuard offboardingLockGuard,
    IEmploymentTypeRepository employmentTypes,
    ICurrentUser currentUser) : IRequestHandler<ChangeEmployeeEmploymentTypeCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(ChangeEmployeeEmploymentTypeCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var employee = await employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<Unit>.NotFound("The employee could not be found.");

        var scopeResult = await manageScopeGuard.EnsureCanManage(tenantId, employee.Id, ct);
        if (scopeResult is not null)
            return Result<Unit>.Forbidden(scopeResult.Error!);

        var lockResult = await offboardingLockGuard.EnsureMutable(tenantId, employee.Id, ct);
        if (lockResult is not null)
            return Result<Unit>.Conflict(lockResult.Error!);

        var employmentTypeId = await employmentTypes.GetIdByCodeAsync(request.EmploymentTypeCode, ct);
        if (employmentTypeId is null)
            return Result<Unit>.Failure("Employment type is invalid.", 400);

        employee.EmploymentTypeId = employmentTypeId.Value;
        await employeeRepository.SaveChangesAsync(ct);
        return Result<Unit>.Success(Unit.Value);
    }
}
