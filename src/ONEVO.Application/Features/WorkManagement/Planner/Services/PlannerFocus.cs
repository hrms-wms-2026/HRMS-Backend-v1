using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>
/// The Focus list (2026-10-27 Planner = Calendar design, section 4.2): what to work on, grouped by due date in the
/// employee's zone and ranked so the tasks that still need planning come first.
/// </summary>
public static class PlannerFocus
{
    public const int MaxItemsPerGroup = 50;

    private static readonly string[] GroupOrder =
        [PlannerFocusReasons.Overdue, PlannerFocusReasons.DueToday, PlannerFocusReasons.DueThisWeek, PlannerFocusReasons.Backlog];

    private static readonly string[] PriorityOrder =
        [WorkTaskPriorities.Critical, WorkTaskPriorities.High, WorkTaskPriorities.Medium, WorkTaskPriorities.Low];

    /// <param name="blocksFromToday">The employee's blocks from the start of today on; earlier ones are ignored.</param>
    public static PlannerFocusResponse Build(IReadOnlyList<PlannerFocusTaskRow> rows, IReadOnlyList<PlannedTaskBlock> blocksFromToday,
        DateOnly today, TimeZoneInfo zone)
    {
        var weekEnd = today.AddDays(7 - (today.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)today.DayOfWeek));
        var plannedByTask = blocksFromToday
            .Select(b => (b.TaskId, Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(b.StartAt, zone).DateTime),
                Minutes: (int)(b.EndAt - b.StartAt).TotalMinutes))
            .Where(b => b.Day >= today)
            .GroupBy(b => b.TaskId)
            .ToDictionary(g => g.Key, g => (Minutes: g.Sum(b => b.Minutes), First: g.Min(b => b.Day), Days: g.Select(b => b.Day).Distinct().Count()));

        var items = rows.Select(row =>
        {
            int? estimate = row.EstimatedHours is { } hours ? (int)Math.Round(hours * 60) : null;
            int? remaining = estimate is { } e ? Math.Max(0, e - row.LoggedMinutes) : null;
            var planned = plannedByTask.TryGetValue(row.TaskId, out var p) ? p : (Minutes: 0, First: (DateOnly?)null, Days: 0);
            return new PlannerFocusItem(row.TaskId, row.ProjectId, row.ProjectName, row.Title, row.Priority, row.DueDate, estimate,
                row.LoggedMinutes, remaining, planned.Minutes, planned.Days > 0 ? planned.First : null, planned.Days,
                FullyPlanned: remaining is { } r && planned.Minutes >= r, Reason(row.DueDate, today, weekEnd));
        }).ToList();

        var groups = GroupOrder.Select(reason =>
        {
            var inGroup = items.Where(i => i.Reason == reason)
                .OrderBy(i => i.FullyPlanned)
                .ThenBy(i => PriorityRank(i.Priority))
                .ThenBy(i => i.DueDate is null)
                .ThenBy(i => i.DueDate)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new PlannerFocusGroup(reason, inGroup.Count, inGroup.Take(MaxItemsPerGroup).ToList());
        }).ToList();

        return new PlannerFocusResponse(today, weekEnd, groups);
    }

    private static string Reason(DateOnly? due, DateOnly today, DateOnly weekEnd)
    {
        if (due is not { } d)
            return PlannerFocusReasons.Backlog;
        if (d < today)
            return PlannerFocusReasons.Overdue;
        if (d == today)
            return PlannerFocusReasons.DueToday;
        return d <= weekEnd ? PlannerFocusReasons.DueThisWeek : PlannerFocusReasons.Backlog;
    }

    private static int PriorityRank(string priority)
    {
        var rank = Array.IndexOf(PriorityOrder, priority);
        return rank < 0 ? PriorityOrder.Length : rank;
    }
}
