using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.AddProjectMember;

public class AddProjectMemberCommandHandler : IRequestHandler<AddProjectMemberCommand, Result<AddObjectiveMemberOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOutboxWriter _outboxWriter;

    public AddProjectMemberCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership,
        IProjectMemberInvitationRepository invitations,
        IUnitOfWork unitOfWork,
        IOutboxWriter outboxWriter)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _objectives = objectives;
        _membership = membership;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
        _outboxWriter = outboxWriter;
    }

    public async Task<Result<AddObjectiveMemberOutcomeResponse>> Handle(AddProjectMemberCommand request, CancellationToken ct)
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

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<AddObjectiveMemberOutcomeResponse>.NotFound("Project not found.");

        if (project.LeadId != callerEmployeeId.Value)
            return Result<AddObjectiveMemberOutcomeResponse>.Forbidden("Only the project owner can add members.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("This project has no default milestone; contact support.");

        if (defaultObjective.IsAchieved)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("Cannot add members to an achieved milestone.");

        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, request.EmployeeId, ct);
        if (assignee is null)
            return Result<AddObjectiveMemberOutcomeResponse>.Failure("The member must be an active employee in this tenant.");

        if (await _membership.HasActiveMembershipAsync(tenantId, defaultObjective.ProjectId, defaultObjective.Id, assignee.Id, ct))
            return Result<AddObjectiveMemberOutcomeResponse>.Success(new AddObjectiveMemberOutcomeResponse(AlreadyMember: true, Invitation: null));

        if (await _invitations.GetPendingForObjectiveAndEmployeeAsync(tenantId, defaultObjective.Id, assignee.Id, ct) is not null)
            return Result<AddObjectiveMemberOutcomeResponse>.Conflict("An invitation is already pending for this employee on this milestone.");

        var invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = defaultObjective.ProjectId,
            ObjectiveId = defaultObjective.Id,
            InvitedEmployeeId = assignee.Id,
            InviteType = ProjectInvitationTypes.Member,
            Status = ProjectInvitationStatuses.Pending,
            InvitedById = callerEmployeeId.Value,
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _invitations.AddAsync(invitation, ct);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [callerEmployeeId.Value], ct);
        var inviterDisplayName = names.GetValueOrDefault(callerEmployeeId.Value) ?? "A teammate";
        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            new WorkNotificationPayload(
                tenantId,
                assignee.UserId,
                "work_project_member_invited",
                new Dictionary<string, string>
                {
                    ["inviterName"] = inviterDisplayName,
                    ["projectName"] = project.Name
                },
                "project_member_invitation",
                invitation.Id),
            tenantId,
            ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<AddObjectiveMemberOutcomeResponse>.Success(
            new AddObjectiveMemberOutcomeResponse(AlreadyMember: false, ProjectMemberInvitationMapper.ToResponse(invitation)));
    }
}
