using MediatR;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;

/// <summary>
/// Reassigns one of an employee's checklist tasks. The endpoint carries no permission code:
/// assigning checklist work (assignee, due date) is open to whoever manages the employee, so the
/// caller must have management coverage over them (the same strict check the offboarding lifecycle
/// uses). Two changes go beyond assigning and still need employees:write: turning a task
/// required/optional (otherwise a manager could skip the bypass-approval flow), and editing tasks
/// on the caller's own record.
/// </summary>
public class UpdateEmployeeChecklistTaskCommandHandler(
    IEmployeeChecklistTaskRepository repository,
    ICurrentUser currentUser,
    IEmployeeOffboardingCoverageGuard coverageGuard,
    IEmployeeRepository employeeRepository)
    : IRequestHandler<UpdateEmployeeChecklistTaskCommand, Result>
{
    private const string EmployeesWrite = PermissionCatalog.Employees.Write;

    public async Task<Result> Handle(UpdateEmployeeChecklistTaskCommand request, CancellationToken ct)
    {
        var canEditEmployees = currentUser.HasPermission(EmployeesWrite);

        var forbidden = await coverageGuard.EnsureCovered(currentUser.TenantId, currentUser.UserId, request.EmployeeId, ct);
        if (forbidden is not null)
            return forbidden;

        if (!canEditEmployees)
        {
            var target = await employeeRepository.GetByIdAsync(currentUser.TenantId, request.EmployeeId, ct);
            if (target is not null && target.UserId == currentUser.UserId)
                return Result.Forbidden("You cannot change checklist tasks on your own record.");
        }

        var task = await repository.GetTrackedByIdAsync(currentUser.TenantId, request.TaskId, ct);
        if (task is null || task.EmployeeId != request.EmployeeId)
            return Result.NotFound("The checklist task could not be found for this employee.");
        if (task.WorkTaskId is not null)
            return Result.Conflict("Update this checklist item through its linked Work task.");
        if (task.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed)
            return Result.Conflict("A completed or bypassed task cannot be edited.");
        if (task.IsRequired != request.IsRequired && !canEditEmployees)
            return Result.Forbidden("Changing whether a checklist task is required needs the employees:write permission.");

        task.AssignedToId = request.AssignedToId;
        task.DueDate = request.DueDate;
        task.IsRequired = request.IsRequired;

        await repository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
