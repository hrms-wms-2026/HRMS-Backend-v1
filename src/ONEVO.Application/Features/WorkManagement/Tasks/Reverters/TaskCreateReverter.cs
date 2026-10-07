using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Reverters;

/// <summary>Undoes an approved task.create by soft-deleting the task it created, and nulls
/// Request.TargetId back to null (TaskCreateApplier stamped it with the new task's id - see Review
/// Focus #2) so the reopened Pending request doesn't keep pointing at a now-deleted task.</summary>
public sealed class TaskCreateReverter : IApprovalActionReverter
{
    private readonly ITaskWriteService _writes;
    private readonly IWorkTaskRepository _tasks;

    public TaskCreateReverter(ITaskWriteService writes, IWorkTaskRepository tasks)
    {
        _writes = writes;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.TaskCreate;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var task = await _tasks.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (task is null)
            return RevertOutcome.Stale;

        _writes.Delete(task);
        request.TargetId = null;
        // No TargetUpdatedAtSnapshot to re-baseline: TaskCreateApplier never checked one (it has no
        // existing target to be stale against), and the request now has no TargetId either.
        return RevertOutcome.Reverted();
    }
}
