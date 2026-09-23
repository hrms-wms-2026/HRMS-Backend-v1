using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectBanner;

/// <summary>Mirrors GetProjectLogoQueryHandler's access rule exactly (projects:read/* permission OR active project membership) so a project's banner is never more visible than the project itself.</summary>
public class GetProjectBannerQueryHandler : IRequestHandler<GetProjectBannerQuery, Result<FileStreamDto>>
{
    private readonly IProjectRepository _projects;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IFileStorageService _fileStorage;

    public GetProjectBannerQueryHandler(
        IProjectRepository projects,
        IEntityAssetRepository entityAssets,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IFileStorageService fileStorage)
    {
        _projects = projects;
        _entityAssets = entityAssets;
        _members = members;
        _permissionResolver = permissionResolver;
        _currentUser = currentUser;
        _identity = identity;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileStreamDto>> Handle(GetProjectBannerQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<FileStreamDto>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<FileStreamDto>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result<FileStreamDto>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
            if (!isMember)
                return Result<FileStreamDto>.Forbidden("You do not have access to this project.");
        }

        var banners = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.Project, [project.Id], UploadPurposeCatalog.ProjectBanner, ct);

        if (!banners.TryGetValue(project.Id, out var fileId))
            return Result<FileStreamDto>.NotFound("Project banner not found.");

        return await _fileStorage.OpenReadAsync(tenantId, fileId, ct);
    }
}
