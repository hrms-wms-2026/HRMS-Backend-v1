namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

/// <summary>The editable fields of a project-template TaskStatus, as seen before or after a change.</summary>
public sealed record TaskStatusSnapshot(string Name, string Category, string Color, string Visibility);

/// <summary>A new status. TempKey is a client-chosen, non-Guid placeholder that Order refers to.</summary>
public sealed record TaskStatusAddChange(string TempKey, string Name, string Category, string Color, string Visibility);

/// <summary>An edit to an existing status. From is what the requester saw; approval fails as stale
/// if the live row no longer matches it.</summary>
public sealed record TaskStatusUpdateChange(Guid StatusId, TaskStatusSnapshot From, TaskStatusSnapshot To);

public sealed record TaskStatusDeleteChange(Guid StatusId, string Name);

/// <summary>
/// A requested batch of changes to a project's status template. BaseOrder is the order of existing
/// statuses the requester started from; Order is the full intended order (existing status ids as
/// Guid strings plus add TempKeys).
/// </summary>
public sealed record TaskStatusChangeSet(
    List<TaskStatusAddChange> Adds,
    List<TaskStatusUpdateChange> Updates,
    List<TaskStatusDeleteChange> Deletes,
    List<Guid> BaseOrder,
    List<string> Order)
{
    public bool IsEmpty => Adds.Count == 0 && Updates.Count == 0 && Deletes.Count == 0 && !ReordersExisting;

    /// <summary>True when the relative order of statuses that existed before (and survive this
    /// change set) differs between BaseOrder and Order. Inserting new statuses alone is not a reorder.</summary>
    public bool ReordersExisting
    {
        get
        {
            var deleted = Deletes.Select(d => d.StatusId).ToHashSet();
            var baseSurvivors = BaseOrder.Where(id => !deleted.Contains(id)).ToList();
            var baseSet = baseSurvivors.ToHashSet();
            var intended = Order
                .Select(key => Guid.TryParse(key, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue && baseSet.Contains(id.Value))
                .Select(id => id!.Value)
                .ToList();
            return !baseSurvivors.SequenceEqual(intended);
        }
    }

    public TaskStatusChangeFootprint Footprint() => new(
        Updates.Select(u => u.StatusId).Concat(Deletes.Select(d => d.StatusId)).ToHashSet(),
        ReordersExisting);
}

/// <summary>
/// What a change touched, for deciding which other pending requests it invalidates: two changes
/// conflict if they edit/delete a common status, or if both reorder existing statuses.
/// </summary>
public sealed record TaskStatusChangeFootprint(IReadOnlySet<Guid> TouchedStatusIds, bool ReordersExisting)
{
    public bool IsEmpty => TouchedStatusIds.Count == 0 && !ReordersExisting;

    public bool ConflictsWith(TaskStatusChangeFootprint other)
        => (ReordersExisting && other.ReordersExisting)
           || TouchedStatusIds.Overlaps(other.TouchedStatusIds);
}
