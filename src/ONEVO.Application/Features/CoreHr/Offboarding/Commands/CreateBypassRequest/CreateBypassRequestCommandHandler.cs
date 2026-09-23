using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CreateBypassRequest;

public class CreateBypassRequestCommandHandler(
    IEmployeeChecklistTaskRepository taskRepository,
    IOffboardingTaskBypassRequestRepository bypassRequestRepository,
    IOffboardingRecordRepository offboardingRecordRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CreateBypassRequestCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateBypassRequestCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        if (request.ApproverId == currentUser.UserId)
            return Result<Guid>.UnprocessableEntity("You cannot approve your own bypass request.");

        var task = await taskRepository.GetTrackedByIdAsync(tenantId, request.TaskId, ct);
        if (task is null || task.EmployeeId != request.EmployeeId)
            return Result<Guid>.NotFound("The checklist task could not be found for this employee.");
        if (!task.IsBypassable)
            return Result<Guid>.UnprocessableEntity("This task cannot be bypassed.");
        if (task.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed)
            return Result<Guid>.Conflict("This task is already resolved.");

        var openRecord = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (openRecord is null)
            return Result<Guid>.Conflict("No open offboarding was found for this employee.");

        var bypassRequest = new OffboardingTaskBypassRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeChecklistTaskId = task.Id,
            OffboardingRecordId = openRecord.Id,
            RequestedById = currentUser.UserId,
            ApproverId = request.ApproverId,
            BypassReason = request.BypassReason,
            PenaltyDescription = request.PenaltyDescription ?? task.BypassPenaltyDescription,
            PriorTaskStatus = task.Status,
            RequestedAt = clock.UtcNow,
        };

        try
        {
            await bypassRequestRepository.AddAsync(bypassRequest, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<Guid>.Conflict("This task already has a pending bypass request.");
        }

        return Result<Guid>.Success(bypassRequest.Id);
    }
}
