using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Helpers;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public class CreateObjectiveCommandHandler : IRequestHandler<CreateObjectiveCommand, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;
    private readonly IProjectMemberInvitationRepository _invitations;

    public CreateObjectiveCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant,
        IProjectMemberInvitationRepository invitations)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
        _invitations = invitations;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(CreateObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveDetailResponse>.Forbidden("No employee record for the current user.");

        var parent = await _objectives.GetByIdForTenantAsync(tenantId, request.ParentObjectiveId, ct);
        if (parent is null || !parent.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Parent objective not found.");

        if (parent.OwnerId != callerEmployeeId.Value)
            return Result<ObjectiveDetailResponse>.Forbidden("Only the parent milestone's head can create a sub-milestone under it.");

        if (ObjectiveParentConstraintChecker.Conflicts(parent, request.StartDate, request.EndDate, request.AllocatedHours))
            return Result<ObjectiveDetailResponse>.Failure(
                "The new milestone's date range or allocated hours would exceed the parent milestone's.");

        var creatorAssignee = await _membership.GetActiveAssigneeAsync(tenantId, callerEmployeeId.Value, ct);
        if (creatorAssignee is null)
            return Result<ObjectiveDetailResponse>.Failure("The creator must be an active employee in this tenant.");

        Employee? proposedHeadAssignee = null;
        if (request.HeadEmployeeId.HasValue && request.HeadEmployeeId.Value != callerEmployeeId.Value)
        {
            proposedHeadAssignee = await _membership.GetActiveAssigneeAsync(tenantId, request.HeadEmployeeId.Value, ct);
            if (proposedHeadAssignee is null)
                return Result<ObjectiveDetailResponse>.Failure("The proposed head must be an active employee in this tenant.");
        }

        var resolvedMemberInvitees = new List<(Guid EmployeeId, string Type)>();
        if (request.MemberInvitations is not null)
        {
            foreach (var invite in request.MemberInvitations)
            {
                if (invite.EmployeeId == callerEmployeeId.Value)
                    continue;

                var inviteeAssignee = await _membership.GetActiveAssigneeAsync(tenantId, invite.EmployeeId, ct);
                if (inviteeAssignee is null)
                    return Result<ObjectiveDetailResponse>.Failure($"Invited member (employee {invite.EmployeeId}) must be an active employee in this tenant.");

                var inviteType = invite.Type == ProjectInvitationTypes.Leader
                    ? ProjectInvitationTypes.Leader
                    : ProjectInvitationTypes.Member;
                resolvedMemberInvitees.Add((invite.EmployeeId, inviteType));
            }
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;

            var objective = new Objective
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = parent.ProjectId,
                ParentObjectiveId = parent.Id,
                IsDefault = false,
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                OwnerId = callerEmployeeId.Value,
                ReportingManagerId = callerEmployeeId.Value,
                IsActive = true,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Progress = 0m,
                AllocatedHours = request.AllocatedHours,
                CompletedHours = 0m,
                CreatedById = userId,
                CreatedAt = now
            };

            await _objectives.AddAsync(objective, innerCt);
            await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, callerEmployeeId.Value, innerCt);
            await _autoGrant.EnsureGrantedAsync(tenantId, userId, userId, "projects:access", innerCt);

            if (proposedHeadAssignee is not null)
            {
                await _invitations.AddAsync(new ProjectMemberInvitation
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                    InvitedEmployeeId = proposedHeadAssignee.Id,
                    InviteType = ProjectInvitationTypes.Leader, Status = ProjectInvitationStatuses.Pending,
                    InvitedById = callerEmployeeId.Value, CreatedById = userId, CreatedAt = now
                }, innerCt);
            }

            foreach (var invitee in resolvedMemberInvitees)
            {
                await _invitations.AddAsync(new ProjectMemberInvitation
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                    InvitedEmployeeId = invitee.EmployeeId,
                    InviteType = invitee.Type, Status = ProjectInvitationStatuses.Pending,
                    InvitedById = callerEmployeeId.Value, CreatedById = userId, CreatedAt = now
                }, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
        }, ct);
    }
}
