using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Mappers;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Commands.RevertWorkApprovalRequest;

public sealed class RevertWorkApprovalRequestCommandHandler
    : IRequestHandler<RevertWorkApprovalRequestCommand, Result<WorkApprovalRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IApprovalActionReverterRegistry _reverters;
    private readonly IWorkNotificationEngine _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public RevertWorkApprovalRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkApprovalRequestRepository requests,
        IApprovalActionReverterRegistry reverters, IWorkNotificationEngine notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _reverters = reverters;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkApprovalRequestResponse>> Handle(RevertWorkApprovalRequestCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkApprovalRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkApprovalRequestResponse>.Forbidden("No employee record for the current user.");
        var caller = callerEmployeeId.Value;

        var request = await _requests.GetTrackedByIdForTenantAsync(tenantId, command.RequestId, ct);
        if (request is null)
            return Result<WorkApprovalRequestResponse>.NotFound("Approval request not found.");

        if (request.DecidedByEmployeeId != caller)
            return Result<WorkApprovalRequestResponse>.Forbidden("Only the person who decided this request can revert it.");
        if (request.Status is not (WorkApprovalRequestStatuses.Approved or WorkApprovalRequestStatuses.Rejected))
            return Result<WorkApprovalRequestResponse>.Conflict("This request is not in a state that can be reverted.");
        var revertableUntil = request.DecidedAt?.AddMinutes(ApprovalRevertWindow.Minutes);
        if (revertableUntil is null || revertableUntil < DateTimeOffset.UtcNow)
            return Result<WorkApprovalRequestResponse>.Conflict("The 30-minute window to revert this decision has passed.");

        Func<DateTimeOffset?>? readTargetUpdatedAt = null;
        if (request.Status == WorkApprovalRequestStatuses.Approved)
        {
            var reverter = _reverters.Find(request.ActionType);
            if (reverter is null)
                return Result<WorkApprovalRequestResponse>.UnprocessableEntity("This request type cannot be reverted.");

            var outcome = await reverter.RevertAsync(new ApprovalRevertContext(request, caller), ct);
            switch (outcome.Kind)
            {
                case RevertOutcomeKind.Stale:
                    return Result<WorkApprovalRequestResponse>.Conflict("What this request changed no longer exists or has moved on.");
                case RevertOutcomeKind.Conflict:
                case RevertOutcomeKind.NotRevertable:
                    return Result<WorkApprovalRequestResponse>.Conflict(outcome.Reason ?? "This request can no longer be reverted.");
            }
            readTargetUpdatedAt = outcome.ReadTargetUpdatedAt;
        }

        try
        {
            var result = await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var now = DateTimeOffset.UtcNow;
                request.RevertedAt = now;
                request.RevertedByEmployeeId = caller;
                request.Status = WorkApprovalRequestStatuses.Pending;
                request.DecidedByEmployeeId = null;
                request.DecisionComment = null;
                request.DecidedAt = null;
                request.AppliedPayloadJson = null;
                request.UndoStateJson = null;
                _requests.Update(request);
                await _unitOfWork.SaveChangesAsync(innerCt);

                // Must happen AFTER the save above: AuditableEntityInterceptor only stamps the real
                // UpdatedAt during SaveChangesAsync, so reading it any earlier would race the interceptor
                // (see Review Focus #1). A second small save persists just this follow-up field.
                if (readTargetUpdatedAt?.Invoke() is { } freshUpdatedAt)
                {
                    request.TargetUpdatedAtSnapshot = freshUpdatedAt;
                    _requests.Update(request);
                    await _unitOfWork.SaveChangesAsync(innerCt);
                }

                await _notifications.NotifyAsync(new WorkNotificationEvent(
                    tenantId, request.ProjectId, caller, WorkNotificationKinds.Reverted, request.ActionType, request.TargetType,
                    request.TargetId, request.TargetTitle, request.Id, [request.RequestedByEmployeeId]), innerCt);

                return Result<WorkApprovalRequestResponse>.Success(null!);
            }, ct);
            if (!result.IsSuccess)
                return result;
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<WorkApprovalRequestResponse>.Conflict(
                "Another request for this same change is already pending - resolve that one first.");
        }

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, [request.RequestedByEmployeeId, request.ApproverEmployeeId], ct);
        return Result<WorkApprovalRequestResponse>.Success(WorkApprovalRequestMapper.ToResponse(request, names, caller));
    }
}
