using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

public interface IMeetingTaskDefaults
{
    /// <summary>The category and placeholder status every meeting task of <paramref name="projectId"/> uses. Adds the
    /// Meeting system category to a project created before it existed (no SaveChanges).</summary>
    Task<Result<(Guid CategoryId, Guid StatusId)>> ResolveAsync(Guid tenantId, Guid projectId, Guid createdById, CancellationToken ct);
}

/// <summary>
/// A meeting task is filed under the project's Meeting system category, and its status_id is only a placeholder - a
/// meeting's status is derived from time (spec D10) - so it takes the project's first Active status. A status that
/// ends up used only by meetings is re-pointed, never left undeletable (final-review I7, Plan B Task 15).
/// </summary>
public sealed class MeetingTaskDefaults : IMeetingTaskDefaults
{
    private readonly ITaskCategoryRepository _categories;
    private readonly ITaskStatusRepository _statuses;

    public MeetingTaskDefaults(ITaskCategoryRepository categories, ITaskStatusRepository statuses)
    {
        _categories = categories;
        _statuses = statuses;
    }

    public async Task<Result<(Guid CategoryId, Guid StatusId)>> ResolveAsync(Guid tenantId, Guid projectId, Guid createdById, CancellationToken ct)
    {
        var status = (await _statuses.GetProjectTemplateAsync(tenantId, projectId, ct))
            .FirstOrDefault(s => s.Category == TaskStatusCategories.Active);
        if (status is null)
            return Result<(Guid, Guid)>.Failure("The project has no active task status.", 400, MeetingErrorCodes.ModuleUnavailable);

        var categories = await _categories.GetByProjectIdAsync(tenantId, projectId, ct);
        var meeting = categories.FirstOrDefault(c => c.SystemKey == TaskCategorySystemKeys.Meeting);
        if (meeting is null)
        {
            meeting = new TaskCategory
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = TaskCategorySystemKeys.Meeting,
                DisplayOrder = categories.Count == 0 ? 0 : categories.Max(c => c.DisplayOrder) + 1,
                SystemKey = TaskCategorySystemKeys.Meeting, CreatedById = createdById, CreatedAt = DateTimeOffset.UtcNow,
            };
            await _categories.AddAsync(meeting, ct);
        }

        return Result<(Guid, Guid)>.Success((meeting.Id, status.Id));
    }
}
