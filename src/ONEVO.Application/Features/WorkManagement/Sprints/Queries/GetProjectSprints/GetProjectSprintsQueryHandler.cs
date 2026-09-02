using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetProjectSprints;

public sealed class GetProjectSprintsQueryHandler : IRequestHandler<GetProjectSprintsQuery, Result<IReadOnlyList<SprintResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ISprintRepository _sprints;

    public GetProjectSprintsQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        ISprintRepository sprints)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _sprints = sprints;
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
        var accessibleObjectiveIds = hasReadPermission
            ? null
            : (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct)).ToHashSet();

        var sprints = await _sprints.GetByProjectAsync(tenantId, project.Id, ct);
        if (accessibleObjectiveIds is not null)
            sprints = sprints.Where(s => accessibleObjectiveIds.Contains(s.ObjectiveId)).ToList();

        return Result<IReadOnlyList<SprintResponse>>.Success(
            sprints.Select(s => new SprintResponse(
                s.Id, s.ObjectiveId, s.Name, s.StartDate, s.EndDate, s.Status, s.CompletedAt, s.AchievedAt)).ToList());
    }
}
