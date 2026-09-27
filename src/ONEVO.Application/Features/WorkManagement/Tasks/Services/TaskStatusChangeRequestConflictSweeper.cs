using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public interface ITaskStatusChangeRequestConflictSweeper
{
    /// <summary>
    /// Marks every pending status change request for the project whose footprint conflicts with a
    /// change that was just applied as Outdated, and notifies its requester. Never calls
    /// SaveChangesAsync - run inside the caller's transaction. Returns how many were outdated.
    /// </summary>
    Task<int> MarkConflictingOutdatedAsync(
        Guid tenantId, Guid projectId, string projectName, TaskStatusChangeFootprint applied,
        Guid? excludingRequestId, CancellationToken ct = default);
}

public sealed class TaskStatusChangeRequestConflictSweeper : ITaskStatusChangeRequestConflictSweeper
{
    public const string OutdatedComment = "Another change to the same statuses was applied first. Resubmit against the current statuses.";

    private readonly ITaskStatusChangeRequestRepository _requests;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;

    public TaskStatusChangeRequestConflictSweeper(
        ITaskStatusChangeRequestRepository requests, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications)
    {
        _requests = requests;
        _membership = membership;
        _notifications = notifications;
    }

    public async Task<int> MarkConflictingOutdatedAsync(
        Guid tenantId, Guid projectId, string projectName, TaskStatusChangeFootprint applied,
        Guid? excludingRequestId, CancellationToken ct = default)
    {
        if (applied.IsEmpty)
            return 0;

        var pending = await _requests.ListTrackedPendingForProjectAsync(tenantId, projectId, ct);
        var now = DateTimeOffset.UtcNow;
        var outdated = 0;

        foreach (var request in pending)
        {
            if (request.Id == excludingRequestId)
                continue;

            var changes = JsonSerializer.Deserialize<TaskStatusChangeSet>(request.ChangesJson);
            if (changes is null || !changes.Footprint().ConflictsWith(applied))
                continue;

            request.Status = TaskStatusChangeRequestStatuses.Outdated;
            request.DecisionComment = OutdatedComment;
            request.DecidedAt = now;
            request.UpdatedAt = now;
            _requests.Update(request);
            outdated++;

            var requester = await _membership.GetActiveAssigneeAsync(tenantId, request.RequestedByEmployeeId, ct);
            if (requester is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId, requester.UserId, "work_task_status_change_request_decided",
                    new Dictionary<string, string> { ["decision"] = "outdated", ["projectName"] = projectName },
                    "task_status_change_request", request.Id, ct);
            }
        }

        return outdated;
    }
}
