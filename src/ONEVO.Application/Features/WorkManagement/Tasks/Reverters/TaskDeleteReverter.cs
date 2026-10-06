using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Reverters;

/// <summary>Undoes an approved task.delete by clearing the soft-delete flags SoftDeleteInterceptor set.</summary>
public sealed class TaskDeleteReverter : IApprovalActionReverter
{
    private readonly ITaskWriteService _writes;
    private readonly IWorkTaskRepository _tasks;

    public TaskDeleteReverter(ITaskWriteService writes, IWorkTaskRepository tasks)
    {
        _writes = writes;
        _tasks = tasks;
    }

    public string ActionType => WorkActionTypes.TaskDelete;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var task = await _tasks.GetTrackedByIdForTenantIncludingDeletedAsync(request.TenantId, request.TargetId.Value, ct);
        if (task is null || !task.IsDeleted)
            return RevertOutcome.Stale;

        _writes.Restore(task);
        return RevertOutcome.Reverted(() => task.UpdatedAt);
    }
}
