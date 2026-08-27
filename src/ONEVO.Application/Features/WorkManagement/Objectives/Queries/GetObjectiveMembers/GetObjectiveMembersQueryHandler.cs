using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveMembers;

public class GetObjectiveMembersQueryHandler : IRequestHandler<GetObjectiveMembersQuery, Result<ObjectiveMemberListResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IPermissionResolver _permissionResolver;

    public GetObjectiveMembersQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectMemberRepository members, IProjectMemberInvitationRepository invitations, IPermissionResolver permissionResolver)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _members = members;
        _invitations = invitations;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<ObjectiveMemberListResponse>> Handle(GetObjectiveMembersQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveMemberListResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveMemberListResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveMemberListResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveMemberListResponse>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var selfAndAncestorIds = new List<Guid> { objective.Id };
            var cursor = objective;
            while (cursor.ParentObjectiveId is not null)
            {
                var parent = await _objectives.GetByIdForTenantAsync(tenantId, cursor.ParentObjectiveId.Value, ct);
                if (parent is null)
                    break;

                selfAndAncestorIds.Add(parent.Id);
                cursor = parent;
            }

            var hasAccess = await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId, callerEmployeeId.Value, selfAndAncestorIds, ct);
            if (!hasAccess)
                return Result<ObjectiveMemberListResponse>.Forbidden("You do not have access to this milestone's members.");
        }

        var activeMembers = await _members.ListActiveForObjectiveAsync(tenantId, objective.Id, ct);
        var pendingInvites = await _invitations.ListPendingForObjectiveAsync(tenantId, objective.Id, ct);

        // Resolved the same way Objective.OwnerName is: bypasses the management-coverage-scoped
        // GET /employees/{id} check (GetEmployeeQueryHandler/EmployeeVisibilityScopeResolver),
        // which 403s for most task assignees since coverage is a People-module reporting-chain
        // concept unrelated to WM project membership. Without this, every member outside the
        // caller's coverage would be unresolvable to a display name on the client.
        var employeeIds = activeMembers.Select(m => m.EmployeeId)
            .Concat(pendingInvites.Select(i => i.InvitedEmployeeId))
            .Distinct()
            .ToList();
        var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, employeeIds, ct);

        var items = new List<ObjectiveMemberItemResponse>();
        items.AddRange(activeMembers.Select(m => new ObjectiveMemberItemResponse(
            m.EmployeeId, namesByEmployeeId.GetValueOrDefault(m.EmployeeId), IsHead: m.EmployeeId == objective.OwnerId,
            Pending: false, InviteType: null, InvitationId: null, SinceOrInvitedAt: m.JoinedAt)));
        items.AddRange(pendingInvites.Select(i => new ObjectiveMemberItemResponse(
            i.InvitedEmployeeId, namesByEmployeeId.GetValueOrDefault(i.InvitedEmployeeId), IsHead: false,
            Pending: true, InviteType: i.InviteType, InvitationId: i.Id, SinceOrInvitedAt: i.CreatedAt)));

        return Result<ObjectiveMemberListResponse>.Success(new ObjectiveMemberListResponse(items));
    }
}
