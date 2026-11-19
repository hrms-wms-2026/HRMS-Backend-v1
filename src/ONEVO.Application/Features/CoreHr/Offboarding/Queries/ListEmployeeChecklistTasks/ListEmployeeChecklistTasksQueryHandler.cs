using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListEmployeeChecklistTasks;

public class ListEmployeeChecklistTasksQueryHandler(
    IOffboardingRecordRepository offboardingRecordRepository,
    IEmployeeChecklistTaskRepository taskRepository,
    ICurrentUser currentUser)
    : IRequestHandler<ListEmployeeChecklistTasksQuery, Result<IReadOnlyList<EmployeeChecklistTaskResponse>>>
{
    public async Task<Result<IReadOnlyList<EmployeeChecklistTaskResponse>>> Handle(ListEmployeeChecklistTasksQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var record = await offboardingRecordRepository.GetLatestByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (record is null)
            return Result<IReadOnlyList<EmployeeChecklistTaskResponse>>.Success(new List<EmployeeChecklistTaskResponse>());

        var tasks = await taskRepository.ListEffectiveByOffboardingRecordAsync(tenantId, record.Id, ct);
        return Result<IReadOnlyList<EmployeeChecklistTaskResponse>>.Success(tasks.Select(row => new EmployeeChecklistTaskResponse(
            row.Task.Id, row.Task.TaskTitle, row.Task.OwnerType, row.Task.AssignedToId, row.Task.DueDate, row.Task.IsRequired,
            row.Task.IsBypassable, row.Task.BypassPenaltyDescription, row.Task.Category,
            row.IsCompleted ? EmployeeChecklistTaskStatuses.Completed : row.Task.Status,
            row.CompletedAt)).ToList());
    }
}
