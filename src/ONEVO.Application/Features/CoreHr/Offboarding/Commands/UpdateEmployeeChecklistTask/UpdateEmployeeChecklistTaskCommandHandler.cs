using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;

public class UpdateEmployeeChecklistTaskCommandHandler(IEmployeeChecklistTaskRepository repository, ICurrentUser currentUser)
    : IRequestHandler<UpdateEmployeeChecklistTaskCommand, Result>
{
    public async Task<Result> Handle(UpdateEmployeeChecklistTaskCommand request, CancellationToken ct)
    {
        var task = await repository.GetTrackedByIdAsync(currentUser.TenantId, request.TaskId, ct);
        if (task is null || task.EmployeeId != request.EmployeeId)
            return Result.NotFound("The checklist task could not be found for this employee.");
        if (task.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed)
            return Result.Conflict("A completed or bypassed task cannot be edited.");

        task.AssignedToId = request.AssignedToId;
        task.DueDate = request.DueDate;
        task.IsRequired = request.IsRequired;

        await repository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
