using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

/// <summary>
/// The links between a materialized meeting occurrence and the ordinary tasks raised from it (spec 4.1, 5.4).
/// A task is the action item of at most one occurrence. Never calls SaveChangesAsync.
/// </summary>
public interface IMeetingActionItemRepository
{
    /// <summary>True when this task is already an action item of some occurrence (one link per task, spec 4.1).</summary>
    Task<bool> ExistsForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    void Add(MeetingActionItem row);

    /// <summary>Every link of one occurrence, untracked.</summary>
    Task<IReadOnlyList<MeetingActionItem>> ListForOccurrenceAsync(Guid tenantId, Guid occurrenceTaskId, CancellationToken ct = default);

    /// <summary>The link between this occurrence and this task, tracked so it can be removed.</summary>
    Task<MeetingActionItem?> GetTrackedAsync(Guid tenantId, Guid occurrenceTaskId, Guid taskId, CancellationToken ct = default);

    void Remove(MeetingActionItem row);
}
