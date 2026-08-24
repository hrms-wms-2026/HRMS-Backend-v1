using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;

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

        var tasks = await taskRepository.ListByOffboardingRecordAsync(tenantId, record.Id, ct);
        return Result<IReadOnlyList<EmployeeChecklistTaskResponse>>.Success(tasks.Select(t => new EmployeeChecklistTaskResponse(
            t.Id, t.TaskTitle, t.OwnerType, t.AssignedToId, t.DueDate, t.IsRequired,
            t.IsBypassable, t.BypassPenaltyDescription, t.Category, t.Status, t.CompletedAt)).ToList());
    }
}
