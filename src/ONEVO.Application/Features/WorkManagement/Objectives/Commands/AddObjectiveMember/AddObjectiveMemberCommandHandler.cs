using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AddObjectiveMember;

public class AddObjectiveMemberCommandHandler : IRequestHandler<AddObjectiveMemberCommand, Result<AddObjectiveMemberOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;

    public AddObjectiveMemberCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, IProjectMemberInvitationRepository invitations, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AddObjectiveMemberOutcomeResponse>> Handle(AddObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<AddObjectiveMemberOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("Cannot add members to an achieved milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Only this milestone's head can add members.");

        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, request.EmployeeId, ct);
        if (assignee is null)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("The member must be an active employee in this tenant.");

        if (await _membership.HasActiveMembershipAsync(tenantId, objective.ProjectId, objective.Id, assignee.Id, ct))
            return Result<AddObjectiveMemberOutcomeResponse>.Success(new AddObjectiveMemberOutcomeResponse(AlreadyMember: true, Invitation: null));

        if (await _invitations.GetPendingForObjectiveAndEmployeeAsync(tenantId, objective.Id, assignee.Id, ct) is not null)
            return Result<AddObjectiveMemberOutcomeResponse>.Conflict("An invitation is already pending for this employee on this milestone.");

        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = objective.ProjectId,
            ObjectiveId = objective.Id,
            InvitedEmployeeId = assignee.Id,
            InviteType = ProjectInvitationTypes.Member,
            Status = ProjectInvitationStatuses.Pending,
            InvitedById = callerEmployeeId.Value,
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _invitations.AddAsync(invitation, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<AddObjectiveMemberOutcomeResponse>.Success(
            new AddObjectiveMemberOutcomeResponse(AlreadyMember: false, ProjectMemberInvitationMapper.ToResponse(invitation)));
    }
}
