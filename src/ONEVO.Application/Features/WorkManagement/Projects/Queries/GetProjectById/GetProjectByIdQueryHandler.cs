using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Helpers;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;

public class GetProjectByIdQueryHandler : IRequestHandler<GetProjectByIdQuery, Result<ProjectDetailResponse>>
{
    private const int MaxLabels = 5;
    private const int MaxMembers = 50;

    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ILabelRepository _labels;
    private readonly IEmployeeRepository _employees;

    public GetProjectByIdQueryHandler(
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        IEntityAssetRepository entityAssets,
        ILabelRepository labels,
        IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _entityAssets = entityAssets;
        _labels = labels;
        _employees = employees;
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

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, userId, ct);
            if (!isMember)
                return Result<ProjectDetailResponse>.Forbidden("You do not have access to this project.");
        }

        var logos = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.Project, [project.Id], UploadPurposeCatalog.ProjectCover, ct);
        var logoFileId = logos.TryGetValue(project.Id, out var fileId) ? fileId : (Guid?)null;

        var labels = await _labels.GetByProjectIdsAsync(tenantId, [project.Id], MaxLabels, ct);
        var projectLabels = labels.TryGetValue(project.Id, out var found) ? found : null;

        var memberUserIdsByProject = await _members.ListDistinctActiveMemberUserIdsAsync(tenantId, [project.Id], MaxMembers, ct);
        var memberCounts = await _members.CountDistinctActiveMembersAsync(tenantId, [project.Id], ct);
        var displayNames = await ProjectMemberAvatarResolver.ResolveDisplayNamesAsync(_employees, tenantId, memberUserIdsByProject, ct);
        var members = ProjectMemberAvatarResolver.BuildAvatars(project.Id, memberUserIdsByProject, displayNames);
        var memberCount = memberCounts.TryGetValue(project.Id, out var count) ? count : 0;

        var isLead = project.LeadId == userId;
        return Result<ProjectDetailResponse>.Success(ProjectMapper.ToDetail(project, isLead, logoFileId, projectLabels, members, memberCount));
    }
}
