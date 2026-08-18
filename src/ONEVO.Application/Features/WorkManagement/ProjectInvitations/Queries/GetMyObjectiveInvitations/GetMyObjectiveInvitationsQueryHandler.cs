using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Queries.GetMyObjectiveInvitations;

public class GetMyObjectiveInvitationsQueryHandler : IRequestHandler<GetMyObjectiveInvitationsQuery, Result<IReadOnlyList<ProjectMemberInvitationResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectMemberInvitationRepository _invitations;

    public GetMyObjectiveInvitationsQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectMemberInvitationRepository invitations)
    {
        _currentUser = currentUser;
        _identity = identity;
        _invitations = invitations;
    }

    public async Task<Result<IReadOnlyList<ProjectMemberInvitationResponse>>> Handle(GetMyObjectiveInvitationsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ProjectMemberInvitationResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ProjectMemberInvitationResponse>>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<ProjectMemberInvitationResponse>>.Forbidden("No employee record for the current user.");

        var pending = await _invitations.ListPendingForEmployeeAsync(tenantId, callerEmployeeId.Value, ct);

        return Result<IReadOnlyList<ProjectMemberInvitationResponse>>.Success(
            pending.Select(ProjectMemberInvitationMapper.ToResponse).ToList());
    }
}
