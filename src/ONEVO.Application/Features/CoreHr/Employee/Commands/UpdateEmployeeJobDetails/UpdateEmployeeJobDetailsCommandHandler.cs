using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmployeeJobDetails;

/// <summary>
/// Admin-facing edit of the three Job & Organizational Details fields that have no other
/// dedicated update flow: EmployeeNumber, EmploymentTypeId (resolved from a stable Code, same
/// contract as onboarding finalize), and WorkModeId. Position/reporting-manager changes stay on
/// ChangeEmployeePositionCommand - this command never touches them.
/// </summary>
public sealed class UpdateEmployeeJobDetailsCommandHandler
    : IRequestHandler<UpdateEmployeeJobDetailsCommand, Result<Unit>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeOffboardingLockGuard _offboardingLockGuard;
    private readonly IEmploymentTypeRepository _employmentTypes;
    private readonly IWorkModeRepository _workModes;
    private readonly ICurrentUser _currentUser;

    public UpdateEmployeeJobDetailsCommandHandler(
        IEmployeeRepository employeeRepository,
        IEmployeeOffboardingLockGuard offboardingLockGuard,
        IEmploymentTypeRepository employmentTypes,
        IWorkModeRepository workModes,
        ICurrentUser currentUser)
    {
        _employeeRepository = employeeRepository;
        _offboardingLockGuard = offboardingLockGuard;
        _employmentTypes = employmentTypes;
        _workModes = workModes;
        _currentUser = currentUser;
    }

    public async Task<Result<Unit>> Handle(UpdateEmployeeJobDetailsCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var employee = await _employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<Unit>.NotFound("The employee could not be found.");

        var lockResult = await _offboardingLockGuard.EnsureMutable(tenantId, employee.Id, ct);
        if (lockResult is not null)
            return Result<Unit>.Conflict(lockResult.Error!);

        var normalizedNumber = EmployeeNumberRules.NormalizeInput(request.EmployeeNumber);
        if (string.IsNullOrEmpty(normalizedNumber) || !EmployeeNumberRules.IsValidFormat(normalizedNumber))
            return Result<Unit>.Failure(EmployeeNumberRules.InvalidFormatMessage, 400);

        var numberInUse = await _employeeRepository.EmployeeNumberExistsAsync(
            tenantId, normalizedNumber, excludeId: employee.Id, ct);
        if (numberInUse)
            return Result<Unit>.Conflict(EmployeeNumberRules.AlreadyInUseMessage);

        var employmentTypeId = await _employmentTypes.GetIdByCodeAsync(request.EmploymentTypeCode, ct);
        if (employmentTypeId is null)
            return Result<Unit>.Failure("Employment type is invalid.", 400);

        if (request.WorkModeId is Guid workModeId)
        {
            var workMode = await _workModes.GetByIdAsync(tenantId, workModeId, ct);
            if (workMode is null || !workMode.IsActive || workMode.LegalEntityId != employee.LegalEntityId)
                return Result<Unit>.Failure("The selected work mode is invalid for this employee's company.", 400);
        }

        employee.EmployeeNumber = normalizedNumber;
        employee.EmploymentTypeId = employmentTypeId.Value;
        employee.WorkModeId = request.WorkModeId;

        await _employeeRepository.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }
}
