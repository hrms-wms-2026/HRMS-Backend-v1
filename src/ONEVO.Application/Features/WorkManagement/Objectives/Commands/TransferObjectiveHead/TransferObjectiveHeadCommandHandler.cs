using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

/// <summary>
/// Reassigns a milestone's head. A milestone with no Reporting Manager gets a leader invitation
/// instead (unchanged); otherwise the transfer goes through the approval engine (the parent's owner
/// or above applies it now; others request).
/// </summary>
public class TransferObjectiveHeadCommandHandler : IRequestHandler<TransferObjectiveHeadCommand, Result<TransferOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IModuleWriteService _modules;
    private readonly IModuleActionSubmitter _submitter;

    public TransferObjectiveHeadCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectMemberInvitationRepository invitations, IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership,
        IOutboxWriter outboxWriter, IModuleWriteService modules, IModuleActionSubmitter submitter)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _outboxWriter = outboxWriter;
        _modules = modules;
        _submitter = submitter;
    }

    public async Task<Result<TransferOutcomeResponse>> Handle(TransferObjectiveHeadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TransferOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<TransferOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<TransferOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<TransferOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<TransferOutcomeResponse>.Failure("The Default Objective's head cannot be transferred.");

        if (objective.IsAchieved)
            return Result<TransferOutcomeResponse>.Failure("An achieved milestone's head cannot be transferred.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<TransferOutcomeResponse>.Forbidden("Only this milestone's head can transfer it.");

        var input = new ModuleTransferInput(request.NewHeadEmployeeId);
        var validation = await _modules.ValidateTransferAsync(tenantId, objective, input, ct);
        if (!validation.IsSuccess)
            return Result<TransferOutcomeResponse>.Failure(validation.Error!, validation.StatusCode ?? 400);

        if (objective.ReportingManagerId is null)
            return await InviteLeaderAsync(tenantId, userId, callerEmployeeId.Value, objective, request.NewHeadEmployeeId, ct);

        var outcome = await _submitter.SubmitAsync(tenantId, callerEmployeeId.Value, objective, WorkActionTypes.ModuleTransfer, input,
            (tracked, innerCt) => _modules.ApplyTransferAsync(tenantId, tracked, input, innerCt),
            extraRecipients: [request.NewHeadEmployeeId], ct: ct);

        return outcome.IsSuccess
            ? Result<TransferOutcomeResponse>.Success(new TransferOutcomeResponse(outcome.Value!.Applied, outcome.Value.ApprovalRequestId, PendingInvitation: null))
            : Result<TransferOutcomeResponse>.Failure(outcome.Error!, outcome.StatusCode ?? 400);
    }

    private async Task<Result<TransferOutcomeResponse>> InviteLeaderAsync(
        Guid tenantId, Guid userId, Guid callerEmployeeId, Objective objective, Guid newHeadEmployeeId, CancellationToken ct)
    {
        var pendingForObjective = await _invitations.ListPendingForObjectiveAsync(tenantId, objective.Id, ct);
        if (pendingForObjective.Any(i => i.InviteType == ProjectInvitationTypes.Leader))
            return Result<TransferOutcomeResponse>.Conflict("A leader invitation is already pending for this milestone.");

        var newHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, newHeadEmployeeId, ct);
        if (newHeadAssignee is null)
            return Result<TransferOutcomeResponse>.Failure("The new head must be an active employee in this tenant.");

        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = objective.ProjectId,
            ObjectiveId = objective.Id,
            InvitedEmployeeId = newHeadAssignee.Id,
            InviteType = ProjectInvitationTypes.Leader,
            Status = ProjectInvitationStatuses.Pending,
            InvitedById = callerEmployeeId,
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _invitations.AddAsync(invitation, ct);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [callerEmployeeId], ct);
        var inviterDisplayName = names.GetValueOrDefault(callerEmployeeId) ?? "A teammate";
        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            new WorkNotificationPayload(
                tenantId,
                newHeadAssignee.UserId,
                "work_objective_invitation_created",
                new Dictionary<string, string>
                {
                    ["inviterName"] = inviterDisplayName,
                    ["objectiveName"] = objective.Title,
                    ["inviteType"] = ProjectInvitationTypes.Leader
                },
                "project_member_invitation",
                invitation.Id),
            tenantId,
            ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<TransferOutcomeResponse>.Success(
            new TransferOutcomeResponse(Applied: false, ApprovalRequestId: null, PendingInvitation: ProjectMemberInvitationMapper.ToResponse(invitation)));
    }
}
