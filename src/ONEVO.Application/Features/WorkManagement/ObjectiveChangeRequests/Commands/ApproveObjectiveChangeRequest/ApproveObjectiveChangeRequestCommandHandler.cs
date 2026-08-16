using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;

public class ApproveObjectiveChangeRequestCommandHandler : IRequestHandler<ApproveObjectiveChangeRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveObjectiveChangeRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveChangeRequestRepository changeRequests,
        IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership,
        IObjectiveAllocationSlackCalculator slack, INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _changeRequests = changeRequests;
        _objectives = objectives;
        _membership = membership;
        _slack = slack;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ApproveObjectiveChangeRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var changeRequest = await _changeRequests.GetByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (changeRequest is null)
            return Result.NotFound("Change request not found.");

        if (changeRequest.ReportingManagerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this request's reporting manager can approve it.");

        if (changeRequest.Status != ObjectiveChangeRequestStatuses.Pending)
            return Result.Conflict("This request has already been decided.");

        var objective = await _objectives.GetTrackedByIdForTenantAsync(tenantId, changeRequest.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;

            switch (changeRequest.RequestType)
            {
                case ObjectiveChangeRequestTypes.Delete:
                    objective.IsActive = false;
                    objective.UpdatedAt = now;
                    break;

                case ObjectiveChangeRequestTypes.Edit:
                    var editPayload = JsonSerializer.Deserialize<EditObjectiveRequestPayload>(changeRequest.PayloadJson!)!;
                    objective.Title = editPayload.Title;
                    objective.Description = editPayload.Description;
                    objective.StartDate = editPayload.StartDate;
                    objective.EndDate = editPayload.EndDate;
                    objective.AllocatedHours = editPayload.AllocatedHours;
                    objective.UpdatedAt = now;
                    break;

                case ObjectiveChangeRequestTypes.Transfer:
                    var transferPayload = JsonSerializer.Deserialize<TransferObjectiveRequestPayload>(changeRequest.PayloadJson!)!;
                    var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, transferPayload.NewHeadEmployeeId, innerCt);
                    if (newHeadAssignee is null)
                        return Result.Failure("The new head must be an active employee in this tenant.");

                    var oldHeadEmployeeId = objective.OwnerId;
                    objective.OwnerId = transferPayload.NewHeadEmployeeId;
                    objective.UpdatedAt = now;

                    var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, innerCt);
                    foreach (var child in directChildren)
                    {
                        child.ReportingManagerId = transferPayload.NewHeadEmployeeId;
                        child.UpdatedAt = now;
                    }

                    await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, transferPayload.NewHeadEmployeeId, innerCt);
                    await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, oldHeadEmployeeId, innerCt);
                    await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, oldHeadEmployeeId, objective.Id, innerCt);
                    break;

                case ObjectiveChangeRequestTypes.Achieve:
                    objective.IsAchieved = true;
                    objective.AchievedAt = now;
                    objective.UpdatedAt = now;
                    await _membership.DeactivateMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, innerCt);
                    await _membership.HasOtherActiveAccessAsync(tenantId, objective.ProjectId, objective.OwnerId, objective.Id, innerCt);
                    break;

                case ObjectiveChangeRequestTypes.Unachieve:
                    var headAssignee = await _membership.GetActiveAssigneeAsync(tenantId, objective.OwnerId, innerCt);
                    if (headAssignee is null)
                        return Result.Failure("The current head must be an active employee in this tenant.");

                    objective.IsAchieved = false;
                    objective.AchievedAt = null;
                    objective.UpdatedAt = now;
                    await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, objective.OwnerId, innerCt);
                    break;

                case ObjectiveChangeRequestTypes.ExtendAllocation:
                    var extendPayload = JsonSerializer.Deserialize<ExtendAllocationRequestPayload>(changeRequest.PayloadJson!)!;
                    if (objective.ParentObjectiveId is null)
                        return Result.Failure("Approver's own milestone could not be resolved.", 422);

                    var approverOwnObjective = await _objectives.GetByIdForTenantAsync(tenantId, objective.ParentObjectiveId.Value, innerCt);
                    if (approverOwnObjective is null)
                        return Result.Failure("Approver's own milestone could not be resolved.", 422);

                    var approverSlack = await _slack.CalculateAsync(tenantId, approverOwnObjective, ct: innerCt);
                    if (extendPayload.RequestedAdditionalHours > approverSlack)
                        return Result.Conflict(
                            "You don't have enough allocation yourself to approve this. Request more from your own reporting manager first, then return to approve this request.");

                    objective.AllocatedHours += extendPayload.RequestedAdditionalHours;
                    objective.UpdatedAt = now;

                    var extendRequester = await _membership.GetActiveAssigneeAsync(tenantId, changeRequest.RequestedById, innerCt);
                    if (extendRequester is not null)
                    {
                        await _notifications.SendTemplatedAsync(
                            tenantId, extendRequester.UserId, "work_allocation_extend_request_decided",
                            new Dictionary<string, string>
                            {
                                ["decision"] = "approved",
                                ["objectiveName"] = objective.Title
                            },
                            "objective_change_request", changeRequest.Id, innerCt);
                    }
                    break;
            }

            _objectives.Update(objective);

            changeRequest.Status = ObjectiveChangeRequestStatuses.Approved;
            changeRequest.DecidedAt = now;
            changeRequest.DecidedById = userId;
            _changeRequests.Update(changeRequest);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result.Success();
        }, ct);
    }
}
