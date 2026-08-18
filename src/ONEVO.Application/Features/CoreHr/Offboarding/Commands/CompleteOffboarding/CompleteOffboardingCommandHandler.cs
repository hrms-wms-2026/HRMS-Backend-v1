using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;

public class CompleteOffboardingCommandHandler(
    IOffboardingRecordRepository offboardingRecordRepository,
    IEmployeeChecklistTaskRepository taskRepository,
    IEmployeeRepository employeeRepository,
    IUserRepository userRepository,
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork,
    IEmployeeOffboardingCoverageGuard coverageGuard,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CompleteOffboardingCommand, Result>
{
    public async Task<Result> Handle(CompleteOffboardingCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var record = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (record is null)
            return Result.NotFound("No open offboarding was found for this employee.");

        var coverageResult = await coverageGuard.EnsureCovered(tenantId, currentUser.UserId, request.EmployeeId, ct);
        if (coverageResult is not null)
            return Result.Forbidden(coverageResult.Error!);
        if (record.Status != OffboardingRecordStatuses.InProgress)
            return Result.Conflict("A checklist must be selected before this offboarding can be completed.");

        var tasks = await taskRepository.ListByOffboardingRecordAsync(tenantId, record.Id, ct);
        if (!OffboardingCompletionGate.AllRequiredTasksResolved(tasks))
            return Result.UnprocessableEntity("Every required checklist task must be completed or bypassed before the exit can be finalized.");

        var employee = await employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result.NotFound("The employee could not be found.");

        var user = await userRepository.GetByIdAsync(employee.UserId, ct);
        if (user is null)
            return Result.NotFound("The employee's user account could not be found.");

        employee.EmploymentStatusId = record.Reason == "resignation" ? EmploymentStatusIds.Resigned : EmploymentStatusIds.Terminated;
        employee.TerminationDate = record.LastWorkingDate;
        user.IsActive = false;
        record.Status = OffboardingRecordStatuses.Completed;
        record.CompletedAt = clock.UtcNow;
        record.UpdatedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);
        await sessionRepository.RevokeAllActiveByUserIdAsync(user.Id, ct);

        return Result.Success();
    }
}
