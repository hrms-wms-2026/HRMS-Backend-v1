using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteEmployeeChecklistTask;

public class CompleteEmployeeChecklistTaskCommandHandler(
    IEmployeeChecklistTaskRepository taskRepository,
    IOffboardingTaskBypassRequestRepository bypassRequestRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CompleteEmployeeChecklistTaskCommand, Result>
{
    public async Task<Result> Handle(CompleteEmployeeChecklistTaskCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var task = await taskRepository.GetTrackedByIdAsync(tenantId, request.TaskId, ct);
        if (task is null || task.EmployeeId != request.EmployeeId)
            return Result.NotFound("The checklist task could not be found for this employee.");
        if (task.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed)
            return Result.Conflict("This task is already resolved.");

        if (await bypassRequestRepository.HasPendingForTaskAsync(tenantId, task.Id, ct))
            return Result.Conflict("This task has a pending bypass request awaiting a decision.");

        task.Status = EmployeeChecklistTaskStatuses.Completed;
        task.CompletedAt = clock.UtcNow;

        await taskRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
