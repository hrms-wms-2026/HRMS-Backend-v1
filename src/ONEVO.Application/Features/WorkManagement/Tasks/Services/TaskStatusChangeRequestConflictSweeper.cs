using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public interface ITaskStatusChangeRequestConflictSweeper
{
    /// <summary>
    /// Closes as stale every pending status-template request in the project whose footprint conflicts
    /// with a change that was just applied, and notifies its requester. Never calls SaveChangesAsync -
    /// run inside the caller's transaction. Returns how many were closed.
    /// </summary>
    Task<int> MarkConflictingStaleAsync(
        Guid tenantId, Guid projectId, Guid actorEmployeeId, TaskStatusChangeFootprint applied,
        Guid? excludingRequestId, CancellationToken ct = default);
}

public sealed class TaskStatusChangeRequestConflictSweeper : ITaskStatusChangeRequestConflictSweeper
{
    public const string OutdatedComment = "Another change to the same statuses was applied first. Resubmit against the current statuses.";
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkNotificationEngine _notifications;

    public TaskStatusChangeRequestConflictSweeper(IWorkApprovalRequestRepository requests, IWorkNotificationEngine notifications)
    {
        _requests = requests;
        _notifications = notifications;
    }

    public async Task<int> MarkConflictingStaleAsync(
        Guid tenantId, Guid projectId, Guid actorEmployeeId, TaskStatusChangeFootprint applied,
        Guid? excludingRequestId, CancellationToken ct = default)
    {
        if (applied.IsEmpty)
            return 0;

        var pending = await _requests.ListTrackedPendingByActionAsync(
            tenantId, projectId, WorkActionTypes.ProjectStatusTemplateChange, ct);
        var now = DateTimeOffset.UtcNow;
        var closed = 0;

        // Id order: two sweeps of the same project take the row locks in one order, so neither waits on the other.
        foreach (var request in pending.OrderBy(r => r.Id))
        {
            if (request.Id == excludingRequestId)
                continue;

            var payload = JsonSerializer.Deserialize<TaskStatusTemplateChangePayload>(request.PayloadJson, PayloadOptions);
            if (payload?.Changes is null || !payload.Changes.Footprint().ConflictsWith(applied))
                continue;

            // The list was read without a lock. Its approver may have decided the request since; the row lock waits for
            // that decision and then finds the row no longer pending, so it is left as decided instead of overwritten.
            if (!await _requests.LockIfPendingAsync(tenantId, request.Id, ct))
                continue;

            request.Status = WorkApprovalRequestStatuses.Stale;
            request.DecisionComment = OutdatedComment;
            request.DecidedAt = now;
            _requests.Update(request);
            closed++;

            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, projectId, actorEmployeeId, WorkNotificationKinds.Stale,
                WorkActionTypes.ProjectStatusTemplateChange, WorkTargetTypes.Project, null,
                request.TargetTitle, request.Id, [request.RequestedByEmployeeId]), ct);
        }

        return closed;
    }
}
