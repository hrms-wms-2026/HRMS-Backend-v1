using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public enum TaskStatusChangeApplyOutcome
{
    Applied,

    /// <summary>The live statuses no longer match what the requester based the change on.</summary>
    Stale,

    /// <summary>Applying would break a template invariant (one Done, at least one Active, unique names).</summary>
    Invalid
}

public sealed record TaskStatusChangeApplyResult(
    TaskStatusChangeApplyOutcome Outcome,
    string? Message,
    IReadOnlyList<TaskStatusEntity> Added,
    IReadOnlyList<TaskStatusEntity> Modified,
    IReadOnlyList<TaskStatusEntity> Deleted,
    IReadOnlyList<TaskStatusEntity> FinalOrder)
{
    public static TaskStatusChangeApplyResult Fail(TaskStatusChangeApplyOutcome outcome, string message)
        => new(outcome, message, [], [], [], []);
}

/// <summary>
/// Pure, in-memory application of a TaskStatusChangeSet onto a project's current status template.
/// Checks staleness first and mutates nothing unless every check passes. The caller persists
/// Added / Modified / Deleted and is responsible for the DB-backed "no active tasks in a deleted
/// status" check before calling.
/// </summary>
public static class TaskStatusChangeSetApplier
{
    public static TaskStatusChangeApplyResult Apply(
        IReadOnlyList<TaskStatusEntity> current,
        TaskStatusChangeSet changes,
        Guid tenantId,
        Guid projectId,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        // Work on copies so a dry run (or a failed apply) never mutates the caller's rows.
        var ordered = current.OrderBy(s => s.DisplayOrder).Select(Clone).ToList();
        var byId = ordered.ToDictionary(s => s.Id);

        foreach (var update in changes.Updates)
        {
            if (!byId.TryGetValue(update.StatusId, out var live))
                return TaskStatusChangeApplyResult.Fail(TaskStatusChangeApplyOutcome.Stale,
                    $"Status \"{update.From.Name}\" no longer exists.");
            if (!Snapshot(live).Equals(update.From))
                return TaskStatusChangeApplyResult.Fail(TaskStatusChangeApplyOutcome.Stale,
                    $"Status \"{update.From.Name}\" was changed after this request was made.");
        }

        foreach (var delete in changes.Deletes)
        {
            if (!byId.ContainsKey(delete.StatusId))
                return TaskStatusChangeApplyResult.Fail(TaskStatusChangeApplyOutcome.Stale,
                    $"Status \"{delete.Name}\" no longer exists.");
        }

        var deleted = changes.Deletes.Select(d => d.StatusId).ToHashSet();

        if (changes.ReordersExisting)
        {
            var baseSet = changes.BaseOrder.ToHashSet();
            var liveBase = ordered.Where(s => baseSet.Contains(s.Id) && !deleted.Contains(s.Id)).Select(s => s.Id);
            var requestedBase = changes.BaseOrder.Where(id => byId.ContainsKey(id) && !deleted.Contains(id));
            if (!liveBase.SequenceEqual(requestedBase))
                return TaskStatusChangeApplyResult.Fail(TaskStatusChangeApplyOutcome.Stale,
                    "The status order was changed after this request was made.");
        }

        // Build the final order: existing survivors first (in requested order when this change set
        // reorders them, otherwise in live order - which keeps statuses added by others meanwhile),
        // then slot each new status in after its nearest preceding neighbour in the requested order.
        var survivors = ordered.Where(s => !deleted.Contains(s.Id)).ToList();
        List<string> sequence;
        if (changes.ReordersExisting)
        {
            var requested = changes.Order
                .Select(key => Guid.TryParse(key, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue && byId.ContainsKey(id.Value) && !deleted.Contains(id.Value))
                .Select(id => id!.Value)
                .ToList();
            var requestedSet = requested.ToHashSet();
            sequence = requested.Select(id => id.ToString())
                .Concat(survivors.Where(s => !requestedSet.Contains(s.Id)).Select(s => s.Id.ToString()))
                .ToList();
        }
        else
        {
            sequence = survivors.Select(s => s.Id.ToString()).ToList();
        }

        var addsByKey = changes.Adds.ToDictionary(a => a.TempKey);
        for (var i = 0; i < changes.Order.Count; i++)
        {
            var key = changes.Order[i];
            if (!addsByKey.ContainsKey(key)) continue;
            var insertAt = 0;
            for (var j = i - 1; j >= 0; j--)
            {
                var anchor = sequence.IndexOf(Normalize(changes.Order[j]));
                if (anchor >= 0) { insertAt = anchor + 1; break; }
            }
            sequence.Insert(insertAt, key);
        }

        var updatesById = changes.Updates.ToDictionary(u => u.StatusId);
        var final = new List<(TaskStatusEntity Status, bool IsNew)>();
        foreach (var key in sequence)
        {
            if (addsByKey.TryGetValue(key, out var add))
            {
                final.Add((new TaskStatusEntity
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = null,
                    Name = add.Name.Trim(), Category = add.Category, Color = add.Color, Visibility = add.Visibility,
                    MarksTaskComplete = add.Category == TaskStatusCategories.Done,
                    CreatedById = createdByUserId, CreatedAt = now
                }, true));
            }
            else
            {
                final.Add((byId[Guid.Parse(key)], false));
            }
        }

        var projected = final.Select(f =>
            !f.IsNew && updatesById.TryGetValue(f.Status.Id, out var u)
                ? (Name: u.To.Name.Trim(), u.To.Category)
                : (Name: f.Status.Name, f.Status.Category)).ToList();

        if (projected.Count(p => p.Category == TaskStatusCategories.Done) != 1)
            return TaskStatusChangeApplyResult.Fail(TaskStatusChangeApplyOutcome.Invalid,
                "A project must always have exactly one Done status.");
        if (!projected.Any(p => p.Category == TaskStatusCategories.Active))
            return TaskStatusChangeApplyResult.Fail(TaskStatusChangeApplyOutcome.Invalid,
                "A project must always have at least one Active status.");
        if (projected.Any(p => string.IsNullOrWhiteSpace(p.Name))
            || projected.Select(p => p.Name.ToLowerInvariant()).Distinct().Count() != projected.Count)
            return TaskStatusChangeApplyResult.Fail(TaskStatusChangeApplyOutcome.Invalid,
                "Status names must be non-empty and unique.");

        var added = new List<TaskStatusEntity>();
        var modified = new List<TaskStatusEntity>();
        for (var order = 0; order < final.Count; order++)
        {
            var (status, isNew) = final[order];
            status.DisplayOrder = order;
            if (isNew)
            {
                added.Add(status);
                continue;
            }

            if (updatesById.TryGetValue(status.Id, out var update))
            {
                status.Name = update.To.Name.Trim();
                status.Category = update.To.Category;
                status.Color = update.To.Color;
                status.Visibility = update.To.Visibility;
                status.MarksTaskComplete = update.To.Category == TaskStatusCategories.Done;
            }
            status.UpdatedAt = now;
            modified.Add(status);
        }

        return new TaskStatusChangeApplyResult(
            TaskStatusChangeApplyOutcome.Applied, null, added, modified,
            ordered.Where(s => deleted.Contains(s.Id)).ToList(),
            final.Select(f => f.Status).ToList());
    }

    public static TaskStatusSnapshot Snapshot(TaskStatusEntity status)
        => new(status.Name, status.Category, status.Color, status.Visibility);

    private static TaskStatusEntity Clone(TaskStatusEntity s) => new()
    {
        Id = s.Id, TenantId = s.TenantId, ProjectId = s.ProjectId, ObjectiveId = s.ObjectiveId, Name = s.Name,
        DisplayOrder = s.DisplayOrder, RequiresApproval = s.RequiresApproval, ApproverId = s.ApproverId,
        MarksTaskComplete = s.MarksTaskComplete, Visibility = s.Visibility, Category = s.Category, Color = s.Color,
        CreatedAt = s.CreatedAt, CreatedById = s.CreatedById, UpdatedAt = s.UpdatedAt,
        IsDeleted = s.IsDeleted, DeletedAt = s.DeletedAt
    };

    private static string Normalize(string key)
        => Guid.TryParse(key, out var id) ? id.ToString() : key;
}
