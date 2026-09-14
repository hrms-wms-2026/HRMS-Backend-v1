using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskFile;

/// <summary>
/// Mirrors GetTaskByIdQueryHandler's access rule exactly (projects:read/* OR
/// active objective membership) for a file already linked to a task, so a
/// task's attachment/inline image is never more visible than the task
/// itself. A file that isn't linked to anything yet (a "pending upload") is
/// visible only to whoever uploaded it.
/// </summary>
public sealed class GetTaskFileQueryHandler : IRequestHandler<GetTaskFileQuery, Result<FileStreamDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IFileRecordRepository _fileRecords;
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IFileStorageService _fileStorage;

    public GetTaskFileQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IEntityAssetRepository entityAssets,
        IFileRecordRepository fileRecords, IWorkTaskRepository tasks, IProjectRepository projects,
        IProjectMemberRepository members, IPermissionResolver permissionResolver, IFileStorageService fileStorage)
    {
        _currentUser = currentUser;
        _identity = identity;
        _entityAssets = entityAssets;
        _fileRecords = fileRecords;
        _tasks = tasks;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileStreamDto>> Handle(GetTaskFileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<FileStreamDto>.Forbidden("Tenant context missing.");

        var link = await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);

        if (link is null)
        {
            var record = await _fileRecords.GetByIdAsync(tenantId, request.FileId, ct);
            if (record is null || record.DeletedAt is not null || record.UploadedByUserId != userId)
                return Result<FileStreamDto>.NotFound("File not found.");

            return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
        }

        if (link.OwnerType != EntityAssetOwnerTypes.Task)
            return Result<FileStreamDto>.NotFound("File not found.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, link.OwnerId, ct);
        if (task is null)
            return Result<FileStreamDto>.NotFound("File not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, task.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<FileStreamDto>.NotFound("File not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission)
        {
            var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
            if (callerEmployeeId is null)
                return Result<FileStreamDto>.NotFound("File not found.");

            var accessibleObjectiveIds =
                (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
                .ToHashSet();
            if (!accessibleObjectiveIds.Contains(task.ObjectiveId))
                return Result<FileStreamDto>.NotFound("File not found.");
        }

        return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
    }
}
