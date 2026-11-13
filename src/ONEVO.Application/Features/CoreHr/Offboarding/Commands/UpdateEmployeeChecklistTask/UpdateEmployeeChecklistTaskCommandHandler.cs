using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;

/// <summary>Reassigns one of an employee's checklist tasks. The endpoint carries no permission
/// code: assigning checklist work is open to whoever manages the employee, so the caller must have
/// management coverage over them (the same strict check the offboarding lifecycle uses).</summary>
public class UpdateEmployeeChecklistTaskCommandHandler(
    IEmployeeChecklistTaskRepository repository,
    ICurrentUser currentUser,
    IEmployeeOffboardingCoverageGuard coverageGuard)
    : IRequestHandler<UpdateEmployeeChecklistTaskCommand, Result>
{
    public async Task<Result> Handle(UpdateEmployeeChecklistTaskCommand request, CancellationToken ct)
    {
        var forbidden = await coverageGuard.EnsureCovered(currentUser.TenantId, currentUser.UserId, request.EmployeeId, ct);
        if (forbidden is not null)
            return forbidden;

        var task = await repository.GetTrackedByIdAsync(currentUser.TenantId, request.TaskId, ct);
        if (task is null || task.EmployeeId != request.EmployeeId)
            return Result.NotFound("The checklist task could not be found for this employee.");
        if (task.WorkTaskId is not null)
            return Result.Conflict("Update this checklist item through its linked Work task.");
        if (task.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed)
            return Result.Conflict("A completed or bypassed task cannot be edited.");

        task.AssignedToId = request.AssignedToId;
        task.DueDate = request.DueDate;
        task.IsRequired = request.IsRequired;

        await repository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
