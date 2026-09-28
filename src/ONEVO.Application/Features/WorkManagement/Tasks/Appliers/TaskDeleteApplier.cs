using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Appliers;

/// <summary>Applies an approved task.delete. Stale if the task is already gone.</summary>
public sealed class TaskDeleteApplier : IApprovalActionApplier
{
    private readonly ITaskWriteService _writes;
    private readonly IWorkTaskRepository _tasks;

    public TaskDeleteApplier(ITaskWriteService writes, IWorkTaskRepository tasks)
    {
        _writes = writes;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.TaskDelete;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return ApplyOutcome.Stale;

        var task = await _tasks.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (task is null)
            return ApplyOutcome.Stale;

        _writes.Delete(task);
        return ApplyOutcome.Applied;
    }
}
