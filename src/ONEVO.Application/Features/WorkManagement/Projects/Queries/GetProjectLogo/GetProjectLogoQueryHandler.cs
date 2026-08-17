using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectLogo;

/// <summary>Mirrors GetProjectByIdQueryHandler's access rule exactly (projects:read/* permission OR active project membership) so a project's logo is never more visible than the project itself.</summary>
public class GetProjectLogoQueryHandler : IRequestHandler<GetProjectLogoQuery, Result<FileStreamDto>>
{
    private readonly IProjectRepository _projects;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorageService _fileStorage;

    public GetProjectLogoQueryHandler(
        IProjectRepository projects,
        IEntityAssetRepository entityAssets,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        ICurrentUser currentUser,
        IFileStorageService fileStorage)
    {
        _projects = projects;
        _entityAssets = entityAssets;
        _members = members;
        _permissionResolver = permissionResolver;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileStreamDto>> Handle(GetProjectLogoQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<FileStreamDto>.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result<FileStreamDto>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, userId, ct);
            if (!isMember)
                return Result<FileStreamDto>.Forbidden("You do not have access to this project.");
        }

        var logos = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.Project, [project.Id], UploadPurposeCatalog.ProjectCover, ct);

        if (!logos.TryGetValue(project.Id, out var fileId))
            return Result<FileStreamDto>.NotFound("Project logo not found.");

        return await _fileStorage.OpenReadAsync(tenantId, fileId, ct);
    }
}
