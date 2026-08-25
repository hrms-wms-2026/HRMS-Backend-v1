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

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskCreationRequest;

public class RejectTaskCreationRequestCommandHandler : IRequestHandler<RejectTaskCreationRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskCreationRequestRepository _requests;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public RejectTaskCreationRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskCreationRequestRepository requests,
        IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _objectives = objectives;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RejectTaskCreationRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var pending = await _requests.GetTrackedByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (pending is null)
            return Result.NotFound("Request not found.");

        if (pending.Status != TaskCreationRequestStatuses.Pending)
            return Result.Conflict("This request has already been decided.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, pending.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result.Forbidden("Only this milestone's owner can decide this request.");

        var payload = JsonSerializer.Deserialize<TaskCreationRequestPayload>(pending.PayloadJson);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            pending.Status = TaskCreationRequestStatuses.Rejected;
            pending.DecidedByEmployeeId = callerEmployeeId.Value;
            pending.DecisionComment = request.Comment.Trim();
            pending.DecidedAt = now;
            pending.UpdatedAt = now;
            _requests.Update(pending);

            var requester = await _membership.GetActiveAssigneeAsync(tenantId, pending.RequestedByEmployeeId, innerCt);
            if (requester is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId, requester.UserId, "work_task_creation_request_decided",
                    new Dictionary<string, string>
                    {
                        ["decision"] = "rejected",
                        ["taskTitle"] = payload?.Title ?? "a task",
                        ["objectiveName"] = objective.Title
                    },
                    "task_creation_request", pending.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
