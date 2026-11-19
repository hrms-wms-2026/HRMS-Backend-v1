using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

using ONEVO.Application.Features.WorkManagement.Hierarchy;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetObjectiveSprints;

public class GetObjectiveSprintsQueryHandler : IRequestHandler<GetObjectiveSprintsQuery, Result<IReadOnlyList<SprintResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ISprintRepository _sprints;
    private readonly IModuleReadAccess _readAccess;

    public GetObjectiveSprintsQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IObjectiveRepository objectives,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        ISprintRepository sprints,
        IModuleReadAccess readAccess)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _members = members;
        _permissionResolver = permissionResolver;
        _sprints = sprints;
        _readAccess = readAccess;
    }

    public async Task<Result<IReadOnlyList<SprintResponse>>> Handle(GetObjectiveSprintsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<SprintResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<SprintResponse>>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<SprintResponse>>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<IReadOnlyList<SprintResponse>>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("*");

        if (!hasReadPermission)
        {
            var hasAccess = await _readAccess.CanReadAsync(tenantId, objective, callerEmployeeId.Value, ct);
            if (!hasAccess)
                return Result<IReadOnlyList<SprintResponse>>.Forbidden("You do not have access to this milestone.");
        }

        var sprints = await _sprints.GetContainingObjectiveTasksAsync(tenantId, request.ObjectiveId, request.ActiveOnly, ct);
        // Any project member may act on a sprint - directly or by request; the approval engine decides which.
        var isMember = await _members.HasActiveMembershipAsync(tenantId, objective.ProjectId, callerEmployeeId.Value, ct);
        return Result<IReadOnlyList<SprintResponse>>.Success(
            sprints.Select(s => SprintResponse.From(s, isMember)).ToList());
    }
}
