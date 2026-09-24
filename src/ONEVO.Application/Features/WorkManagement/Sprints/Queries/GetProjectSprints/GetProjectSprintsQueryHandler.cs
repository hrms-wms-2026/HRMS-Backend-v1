using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetProjectSprints;

public sealed class GetProjectSprintsQueryHandler : IRequestHandler<GetProjectSprintsQuery, Result<IReadOnlyList<SprintResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ISprintRepository _sprints;
    private readonly ISprintAccessService _access;

    public GetProjectSprintsQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        ISprintRepository sprints,
        ISprintAccessService access)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _sprints = sprints;
        _access = access;
    }

    public async Task<Result<IReadOnlyList<SprintResponse>>> Handle(GetProjectSprintsQuery request, CancellationToken ct)
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

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<SprintResponse>>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission && !await _members.HasActiveMembershipAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
            return Result<IReadOnlyList<SprintResponse>>.Forbidden("You do not have access to this project.");

        var sprints = await _sprints.GetByProjectAsync(tenantId, project.Id, ct);
        var manageable = await _access.GetManageableSprintIdsAsync(tenantId, project.Id, sprints, userId, callerEmployeeId.Value, ct);

        return Result<IReadOnlyList<SprintResponse>>.Success(
            sprints.Select(s => SprintResponse.From(s, manageable.Contains(s.Id))).ToList());
    }
}
