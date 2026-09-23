using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CancelOffboarding;

public class CancelOffboardingCommandHandler(
    IOffboardingRecordRepository offboardingRecordRepository,
    IEmployeeRepository employeeRepository,
    IEmployeeOffboardingCoverageGuard coverageGuard,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CancelOffboardingCommand, Result>
{
    public async Task<Result> Handle(CancelOffboardingCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var record = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (record is null)
            return Result.NotFound("No open offboarding was found for this employee.");

        var coverageResult = await coverageGuard.EnsureCovered(tenantId, currentUser.UserId, request.EmployeeId, ct);
        if (coverageResult is not null)
            return Result.Forbidden(coverageResult.Error!);

        var employee = await employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result.NotFound("The employee could not be found.");

        employee.EmploymentStatusId = record.PreviousEmploymentStatusId ?? EmploymentStatusIds.Active;
        record.Status = OffboardingRecordStatuses.Cancelled;
        record.UpdatedAt = clock.UtcNow;

        await offboardingRecordRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
