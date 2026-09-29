using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Helpers;

/// <summary>Shared by ListProjectsQueryHandler and GetProjectByIdQueryHandler: turns a per-project capped list of active member employee ids into avatar DTOs with resolved display names, batching the employee lookup across every project on the page in one call. EmployeeId-keyed (Phase 2, 2026-08-14) - resolves names via ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync rather than a direct UserId-keyed repository call.</summary>
public static class ProjectMemberAvatarResolver
{
    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveDisplayNamesAsync(
        ICallerIdentityResolver identity, Guid tenantId,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> memberEmployeeIdsByProject, CancellationToken ct)
    {
        var allEmployeeIds = memberEmployeeIdsByProject.Values.SelectMany(ids => ids).Distinct().ToList();
        if (allEmployeeIds.Count == 0)
            return new Dictionary<Guid, string>();

        return await identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, allEmployeeIds, ct);
    }

    /// <summary>
    /// Batched employee avatar lookup, mirroring GetEmployeeQueryHandler/ListEmployeesQueryHandler's
    /// own EntityAssetOwnerTypes.Employee + UploadPurposeCatalog.EmployeeAvatar lookup, so a
    /// project's member avatars come from the same centralized file API as everywhere else.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, Guid>> ResolveAvatarFileIdsAsync(
        IEntityAssetRepository entityAssets, Guid tenantId,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> memberEmployeeIdsByProject, CancellationToken ct)
    {
        var allEmployeeIds = memberEmployeeIdsByProject.Values.SelectMany(ids => ids).Distinct().ToList();
        if (allEmployeeIds.Count == 0)
            return new Dictionary<Guid, Guid>();

        return await entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.Employee, allEmployeeIds, UploadPurposeCatalog.EmployeeAvatar, ct);
    }

    public static IReadOnlyList<ProjectMemberAvatarDto> BuildAvatars(
        Guid projectId, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> memberEmployeeIdsByProject,
        IReadOnlyDictionary<Guid, string> displayNames, IReadOnlyDictionary<Guid, Guid> avatarFileIds)
    {
        if (!memberEmployeeIdsByProject.TryGetValue(projectId, out var employeeIds))
            return [];

        return employeeIds
            .Select(employeeId => new ProjectMemberAvatarDto(
                employeeId,
                displayNames.TryGetValue(employeeId, out var name) ? name : "Unknown",
                avatarFileIds.TryGetValue(employeeId, out var fileId) ? fileId : null))
            .ToList();
    }
}
