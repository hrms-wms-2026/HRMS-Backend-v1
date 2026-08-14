using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Helpers;

/// <summary>Shared by ListProjectsQueryHandler and GetProjectByIdQueryHandler: turns a per-project capped list of active member user ids into avatar DTOs with resolved display names, batching the employee lookup across every project on the page in one call.</summary>
public static class ProjectMemberAvatarResolver
{
    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveDisplayNamesAsync(
        IEmployeeRepository employees, Guid tenantId,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> memberUserIdsByProject, CancellationToken ct)
    {
        var allUserIds = memberUserIdsByProject.Values.SelectMany(ids => ids).Distinct().ToList();
        if (allUserIds.Count == 0)
            return new Dictionary<Guid, string>();

        var records = await employees.GetByUserIdsAsync(tenantId, allUserIds, ct);
        return records.ToDictionary(e => e.UserId, e => $"{e.FirstName} {e.LastName}".Trim());
    }

    public static IReadOnlyList<ProjectMemberAvatarDto> BuildAvatars(
        Guid projectId, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> memberUserIdsByProject,
        IReadOnlyDictionary<Guid, string> displayNames)
    {
        if (!memberUserIdsByProject.TryGetValue(projectId, out var userIds))
            return [];

        return userIds
            .Select(userId => new ProjectMemberAvatarDto(userId, displayNames.TryGetValue(userId, out var name) ? name : "Unknown"))
            .ToList();
    }
}
