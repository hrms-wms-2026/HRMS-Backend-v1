using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetProjectSprints;

public sealed class GetProjectSprintsQueryHandler : IRequestHandler<GetProjectSprintsQuery, Result<IReadOnlyList<SprintResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ISprintRepository _sprints;

    private const string NotFoundForDelegate = "Project not found.";
    private readonly IDelegateAccess? _delegateAccess;

    public GetProjectSprintsQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        ISprintRepository sprints,
        IDelegateAccess? delegateAccess = null)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _sprints = sprints;
        _delegateAccess = delegateAccess;
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

        // Delegate task planning (2026-11-11): a planning delegate reads as the principal (their ids go into the access
        // checks below; the signed-in user is unchanged), and only within the principal's assigned work.
        var subjectEmployeeId = callerEmployeeId.Value;
        var subjectUserId = userId;
        if (request.ForEmployeeId is Guid forEmployeeId && forEmployeeId != callerEmployeeId.Value)
        {
            if (_delegateAccess is null
                || await _delegateAccess.PlanningSubjectAsync(tenantId, callerEmployeeId.Value, forEmployeeId, ct) is not { } principal
                || !await _delegateAccess.CanReadProjectAsync(tenantId, principal.EmployeeId, request.ProjectId, ct))
                return Result<IReadOnlyList<SprintResponse>>.NotFound(NotFoundForDelegate);
            (subjectEmployeeId, subjectUserId) = (principal.EmployeeId, principal.UserId);
        }

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<SprintResponse>>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(subjectUserId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("*");
        if (!hasReadPermission && !await _members.HasActiveMembershipAsync(tenantId, project.Id, subjectEmployeeId, ct))
            return Result<IReadOnlyList<SprintResponse>>.Forbidden("You do not have access to this project.");

        var sprints = await _sprints.GetByProjectAsync(tenantId, project.Id, ct);
        // Any project member may act on a sprint - directly or by request; the approval engine decides which.
        var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, subjectEmployeeId, ct);

        return Result<IReadOnlyList<SprintResponse>>.Success(
            sprints.Select(s => SprintResponse.From(s, isMember)).ToList());
    }
}
