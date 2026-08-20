using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

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

    public AddProjectMemberCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership,
        IProjectMemberInvitationRepository invitations,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _objectives = objectives;
        _membership = membership;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
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

        throw new NotImplementedException();
    }
}
