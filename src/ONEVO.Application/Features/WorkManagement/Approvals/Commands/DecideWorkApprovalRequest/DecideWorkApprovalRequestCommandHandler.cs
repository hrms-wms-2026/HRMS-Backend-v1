using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Mappers;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;

public sealed class DecideWorkApprovalRequestCommandHandler
    : IRequestHandler<DecideWorkApprovalRequestCommand, Result<WorkApprovalRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IApprovalActionApplierRegistry _appliers;
    private readonly IWorkNotificationEngine _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public DecideWorkApprovalRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkApprovalRequestRepository requests,
        IWorkHierarchyService hierarchy, IApprovalActionApplierRegistry appliers,
        IWorkNotificationEngine notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _hierarchy = hierarchy;
        _appliers = appliers;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkApprovalRequestResponse>> Handle(DecideWorkApprovalRequestCommand command, CancellationToken ct)
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
        if (request.Status != WorkApprovalRequestStatuses.Pending)
            return Result<WorkApprovalRequestResponse>.Conflict("This request has already been decided.");

        if (command.Decision == WorkApprovalDecision.Cancel)
        {
            if (request.RequestedByEmployeeId != caller)
                return Result<WorkApprovalRequestResponse>.Forbidden("Only the person who made this request can cancel it.");
        }
        else
        {
            var tree = await _hierarchy.LoadTreeAsync(tenantId, request.ProjectId, ct);
            if (!WorkApprovalDecisionRules.CanDecide(tree, request, caller))
                return Result<WorkApprovalRequestResponse>.Forbidden("You are not the approver for this request.");
        }

        IApprovalActionApplier? applier = null;
        if (command.Decision == WorkApprovalDecision.Approve)
        {
            applier = _appliers.Find(request.ActionType);
            if (applier is null)
                return Result<WorkApprovalRequestResponse>.UnprocessableEntity("This request type can no longer be approved.");
        }

        var payload = command.Decision == WorkApprovalDecision.Approve && !string.IsNullOrWhiteSpace(command.EditedPayloadJson)
            ? command.EditedPayloadJson!
            : request.PayloadJson;

        var result = await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            string status, kind;
            switch (command.Decision)
            {
                case WorkApprovalDecision.Approve:
                    var outcome = await applier!.ApplyAsync(new ApprovalApplyContext(request, payload, caller), innerCt);
                    if (outcome.Kind == ApplyOutcomeKind.Invalid)
                        return Result<WorkApprovalRequestResponse>.UnprocessableEntity(outcome.Error ?? "The requested change is not valid.");
                    (status, kind) = outcome.Kind == ApplyOutcomeKind.Applied
                        ? (WorkApprovalRequestStatuses.Approved, WorkNotificationKinds.Approved)
                        : (WorkApprovalRequestStatuses.Stale, WorkNotificationKinds.Stale);
                    if (outcome.Kind == ApplyOutcomeKind.Applied)
                        request.PayloadJson = payload;
                    break;
                case WorkApprovalDecision.Reject:
                    (status, kind) = (WorkApprovalRequestStatuses.Rejected, WorkNotificationKinds.Rejected);
                    break;
                default:
                    (status, kind) = (WorkApprovalRequestStatuses.Cancelled, WorkNotificationKinds.Cancelled);
                    break;
            }

            request.Status = status;
            request.DecidedByEmployeeId = caller;
            request.DecisionComment = command.Comment?.Trim();
            request.DecidedAt = DateTimeOffset.UtcNow;
            _requests.Update(request);

            var notifyWho = command.Decision == WorkApprovalDecision.Cancel ? request.ApproverEmployeeId : request.RequestedByEmployeeId;
            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, request.ProjectId, caller, kind, request.ActionType, request.TargetType,
                request.TargetId, request.TargetTitle, request.Id, [notifyWho]), innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<WorkApprovalRequestResponse>.Success(null!);
        }, ct);

        if (!result.IsSuccess)
            return result;

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, [request.RequestedByEmployeeId, request.ApproverEmployeeId], ct);
        return Result<WorkApprovalRequestResponse>.Success(WorkApprovalRequestMapper.ToResponse(request, names));
    }
}
