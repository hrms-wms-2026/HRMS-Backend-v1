using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskStatusChangeRequest;

public class RejectTaskStatusChangeRequestCommandHandler : IRequestHandler<RejectTaskStatusChangeRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusChangeRequestRepository _requests;
    private readonly ITaskStatusChangeAccessService _access;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public RejectTaskStatusChangeRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        ITaskStatusChangeRequestRepository requests, ITaskStatusChangeAccessService access,
        IMilestoneMembershipCoordinator membership, INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _requests = requests;
        _access = access;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RejectTaskStatusChangeRequestCommand request, CancellationToken ct)
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
        if (pending.Status != TaskStatusChangeRequestStatuses.Pending)
            return Result.Conflict("This request has already been decided.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, pending.ProjectId, ct);
        if (project is null)
            return Result.NotFound("Project not found.");

        var access = await _access.ResolveAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
        if (access is null || !access.CanEditDirectly)
            return Result.Forbidden("Only the project's top module owner or its members can decide this request.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            pending.Status = TaskStatusChangeRequestStatuses.Rejected;
            pending.DecidedByEmployeeId = callerEmployeeId.Value;
            pending.DecisionComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
            pending.DecidedAt = now;
            pending.UpdatedAt = now;
            _requests.Update(pending);

            var requester = await _membership.GetActiveAssigneeAsync(tenantId, pending.RequestedByEmployeeId, innerCt);
            if (requester is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId, requester.UserId, "work_task_status_change_request_decided",
                    new Dictionary<string, string> { ["decision"] = "rejected", ["projectName"] = project.Name },
                    "task_status_change_request", pending.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
