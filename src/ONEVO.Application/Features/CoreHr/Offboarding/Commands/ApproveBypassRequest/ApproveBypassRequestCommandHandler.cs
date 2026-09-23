using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;

public class ApproveBypassRequestCommandHandler(
    IOffboardingTaskBypassRequestRepository bypassRequestRepository,
    IEmployeeChecklistTaskRepository taskRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ApproveBypassRequestCommand, Result>
{
    public async Task<Result> Handle(ApproveBypassRequestCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var bypassRequest = await bypassRequestRepository.GetTrackedByIdAsync(tenantId, request.BypassRequestId, ct);
        if (bypassRequest is null)
            return Result.NotFound("The bypass request could not be found.");
        if (bypassRequest.ApproverId != currentUser.UserId)
            return Result.Forbidden("Only the assigned approver can decide this request.");
        if (bypassRequest.Status != BypassRequestStatuses.Pending)
            return Result.Conflict("This bypass request has already been decided.");

        var task = await taskRepository.GetTrackedByIdAsync(tenantId, bypassRequest.EmployeeChecklistTaskId, ct);
        if (task is null)
            return Result.NotFound("The checklist task for this bypass request could not be found.");

        task.Status = EmployeeChecklistTaskStatuses.Bypassed;
        task.CompletedAt = clock.UtcNow;
        bypassRequest.Status = BypassRequestStatuses.Approved;
        bypassRequest.DecidedAt = clock.UtcNow;

        await bypassRequestRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
