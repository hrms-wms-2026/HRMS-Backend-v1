using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;

public class GetProjectByIdQueryHandler : IRequestHandler<GetProjectByIdQuery, Result<ProjectDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;

    public GetProjectByIdQueryHandler(
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver)
    {
        _currentUser = currentUser;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<ProjectDetailResponse>> Handle(GetProjectByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ProjectDetailResponse>.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<ProjectDetailResponse>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, userId, ct);
            if (!isMember)
                return Result<ProjectDetailResponse>.Forbidden("You do not have access to this project.");
        }

        var isLead = project.LeadId == userId;
        return Result<ProjectDetailResponse>.Success(ProjectMapper.ToDetail(project, isLead));
    }
}
