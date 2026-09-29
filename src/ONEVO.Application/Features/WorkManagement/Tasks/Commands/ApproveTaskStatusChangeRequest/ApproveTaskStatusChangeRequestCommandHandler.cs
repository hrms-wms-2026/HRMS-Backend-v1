using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskStatusChangeRequest;

public class ApproveTaskStatusChangeRequestCommandHandler
    : IRequestHandler<ApproveTaskStatusChangeRequestCommand, Result<IReadOnlyList<TaskStatusResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusChangeRequestRepository _requests;
    private readonly ITaskStatusChangeAccessService _access;
    private readonly ITaskStatusChangeRequestConflictSweeper _sweeper;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveTaskStatusChangeRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        ITaskStatusRepository statuses, IWorkTaskRepository tasks, ITaskStatusChangeRequestRepository requests,
        ITaskStatusChangeAccessService access, ITaskStatusChangeRequestConflictSweeper sweeper,
        IMilestoneMembershipCoordinator membership, INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _statuses = statuses;
        _tasks = tasks;
        _requests = requests;
        _access = access;
        _sweeper = sweeper;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<TaskStatusResponse>>> Handle(
        ApproveTaskStatusChangeRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("No employee record for the current user.");

        var pending = await _requests.GetTrackedByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (pending is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Request not found.");
        if (pending.Status != TaskStatusChangeRequestStatuses.Pending)
            return Result<IReadOnlyList<TaskStatusResponse>>.Conflict("This request has already been decided.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, pending.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Project not found.");

        var access = await _access.ResolveAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
        if (access is null || !access.CanEditDirectly)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden(
                "Only the project's top module owner or its members can decide this request.");

        var changes = JsonSerializer.Deserialize<TaskStatusChangeSet>(pending.ChangesJson)!;

        // Tasks still sitting in a status to delete block approval but don't make the request stale:
        // it stays pending so it can be approved once the tasks are moved, or rejected.
        foreach (var delete in changes.Deletes)
        {
            if (await _tasks.AnyActiveByStatusIdAsync(tenantId, delete.StatusId, ct))
                return Result<IReadOnlyList<TaskStatusResponse>>.Conflict(
                    $"Move all tasks out of \"{delete.Name}\" before approving its deletion.");
        }

        var current = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var now = DateTimeOffset.UtcNow;
        var applied = TaskStatusChangeSetApplier.Apply(current, changes, tenantId, project.Id, _currentUser.UserId, now);

        if (applied.Outcome == TaskStatusChangeApplyOutcome.Invalid)
            return Result<IReadOnlyList<TaskStatusResponse>>.Conflict(applied.Message!);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var requester = await _membership.GetActiveAssigneeAsync(tenantId, pending.RequestedByEmployeeId, innerCt);

            if (applied.Outcome == TaskStatusChangeApplyOutcome.Stale)
            {
                pending.Status = TaskStatusChangeRequestStatuses.Outdated;
                pending.DecisionComment = applied.Message;
                pending.DecidedAt = now;
                pending.UpdatedAt = now;
                _requests.Update(pending);
                if (requester is not null)
                    await NotifyRequesterAsync(tenantId, requester.UserId, "outdated", project.Name, pending.Id, innerCt);
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<IReadOnlyList<TaskStatusResponse>>.Conflict(
                    applied.Message + " The request has been closed as outdated.");
            }

            foreach (var status in applied.Added)
                await _statuses.AddAsync(status, innerCt);
            foreach (var status in applied.Modified)
                _statuses.Update(status);
            foreach (var status in applied.Deleted)
                _statuses.Remove(status);

            pending.Status = TaskStatusChangeRequestStatuses.Approved;
            pending.DecidedByEmployeeId = callerEmployeeId.Value;
            pending.DecidedAt = now;
            pending.UpdatedAt = now;
            _requests.Update(pending);

            await _sweeper.MarkConflictingOutdatedAsync(
                tenantId, project.Id, project.Name, changes.Footprint(), pending.Id, innerCt);

            if (requester is not null)
                await NotifyRequesterAsync(tenantId, requester.UserId, "approved", project.Name, pending.Id, innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<IReadOnlyList<TaskStatusResponse>>.Success(applied.FinalOrder
                .Select(s => new TaskStatusResponse(s.Id, s.Name, s.DisplayOrder, s.RequiresApproval, s.ApproverId,
                    s.MarksTaskComplete, s.Visibility, s.Category, s.Color))
                .ToList());
        }, ct);
    }

    private Task NotifyRequesterAsync(Guid tenantId, Guid userId, string decision, string projectName, Guid requestId, CancellationToken ct)
        => _notifications.SendTemplatedAsync(
            tenantId, userId, "work_task_status_change_request_decided",
            new Dictionary<string, string> { ["decision"] = decision, ["projectName"] = projectName },
            "task_status_change_request", requestId, ct);
}
