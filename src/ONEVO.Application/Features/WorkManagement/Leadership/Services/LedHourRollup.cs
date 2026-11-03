using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>Team Progress in estimated hours: rolls each Module's own ModuleHourSummary row up its
/// subtree and sets it against the Module's allocation. A child Module's allocation is part of its
/// parent's (CreateObjective caps it at the parent's), so a Module's untasked hours are its own
/// allocation minus the task hours of its whole subtree.</summary>
public static class LedHourRollup
{
    /// <summary>The head followed depth-first by its child Modules (siblings by title).</summary>
    public static IReadOnlyList<LedModuleProgress> ForHead(
        LedHeadModule head, IReadOnlyDictionary<Guid, ModuleHourSummary> summaries, DateOnly today)
    {
        var rows = head.SubtreeRows ?? [];
        var rowsById = rows.ToDictionary(r => r.Id);
        var childrenOf = rows
            .Where(r => r.Id != head.ObjectiveId && r.ParentObjectiveId is Guid parent && rowsById.ContainsKey(parent))
            .GroupBy(r => r.ParentObjectiveId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList());

        var result = new List<LedModuleProgress>();
        Visit(head.ObjectiveId, parent: null, depth: 0);
        return result;

        // Returns the subtree's raw sums so the parent can add them without re-walking.
        (decimal Completed, decimal InProgress, decimal NotStarted, int Tasks, int Unestimated, bool AtRisk) Visit(
            Guid id, Guid? parent, int depth)
        {
            var index = result.Count;
            result.Add(null!); // reserve the slot so a parent precedes its children

            var own = summaries.GetValueOrDefault(id);
            var completed = own?.CompletedHours ?? 0;
            var inProgress = own?.InProgressHours ?? 0;
            var notStarted = own?.NotStartedHours ?? 0;
            var tasks = own?.TaskCount ?? 0;
            var unestimated = own?.UnestimatedTaskCount ?? 0;
            var atRisk = own?.EarliestOpenDueDate is { } due && due < today;

            foreach (var child in childrenOf.GetValueOrDefault(id) ?? [])
            {
                var sub = Visit(child.Id, id, depth + 1);
                completed += sub.Completed;
                inProgress += sub.InProgress;
                notStarted += sub.NotStarted;
                tasks += sub.Tasks;
                unestimated += sub.Unestimated;
                atRisk |= sub.AtRisk;
            }

            var row = rowsById.GetValueOrDefault(id);
            var allocated = id == head.ObjectiveId ? head.AllocatedHours : row?.AllocatedHours ?? 0;
            var tasked = completed + inProgress + notStarted;
            var hours = new LedHourTotals(
                allocated, completed, inProgress, notStarted,
                Math.Max(0, allocated - tasked), Math.Max(0, tasked - allocated), tasks, unestimated);

            result[index] = new LedModuleProgress(
                id,
                id == head.ObjectiveId ? head.Title : row?.Title ?? string.Empty,
                id == head.ObjectiveId ? head.IsRootModule : row?.IsDefault ?? false,
                id == head.ObjectiveId ? head.EndDate : row?.EndDate ?? head.EndDate,
                parent, depth, atRisk, hours);
            return (completed, inProgress, notStarted, tasks, unestimated, atRisk);
        }
    }

    public static LedHourTotals Add(IEnumerable<LedHourTotals> parts) => parts.Aggregate(LedHourTotals.Zero, (a, b) => new LedHourTotals(
        a.AllocatedHours + b.AllocatedHours,
        a.CompletedHours + b.CompletedHours,
        a.InProgressHours + b.InProgressHours,
        a.NotStartedHours + b.NotStartedHours,
        a.UntaskedHours + b.UntaskedHours,
        a.OverTaskedHours + b.OverTaskedHours,
        a.TaskCount + b.TaskCount,
        a.UnestimatedTaskCount + b.UnestimatedTaskCount));
}
