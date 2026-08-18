using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskEditRequest;

public class RejectTaskEditRequestCommandHandler
    : IRequestHandler<RejectTaskEditRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskEditRequestRepository _requests;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public RejectTaskEditRequestCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        ITaskEditRequestRepository requests,
        IWorkTaskRepository tasks,
        IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _tasks = tasks;
        _objectives = objectives;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        RejectTaskEditRequestCommand request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(
            tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var pending = await _requests.GetTrackedByIdForTenantAsync(
            tenantId, request.RequestId, ct);
        if (pending is null)
            return Result.NotFound("Request not found.");

        if (pending.Status != TaskEditRequestStatuses.Pending)
            return Result.Conflict("This request has already been decided.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, pending.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(
            tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this milestone's owner can decide this request.");

        var payload = JsonSerializer.Deserialize<TaskEditRequestPayload>(pending.PayloadJson);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            pending.Status = TaskEditRequestStatuses.Rejected;
            pending.DecidedByEmployeeId = callerEmployeeId.Value;
            pending.DecisionComment = request.Comment.Trim();
            pending.DecidedAt = now;
            pending.UpdatedAt = now;
            _requests.Update(pending);

            var requester = await _membership.GetActiveAssigneeAsync(
                tenantId, pending.RequestedByEmployeeId, innerCt);
            if (requester is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId,
                    requester.UserId,
                    "work_task_edit_request_decided",
                    new Dictionary<string, string>
                    {
                        ["decision"] = "rejected",
                        ["taskTitle"] = payload?.Title ?? "a task",
                        ["objectiveName"] = objective.Title
                    },
                    "task_edit_request",
                    pending.Id,
                    innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
