using System.Globalization;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

/// <summary>
/// The project monitor's rules: pure functions over a snapshot. They only predict and detect - they
/// never block anything. Used by the live form checks and by the hourly monitor.
/// </summary>
public static class ProjectMonitorRules
{
    public static IReadOnlyList<MonitorFinding> Evaluate(ProjectMonitorSnapshot snapshot, DateOnly today)
    {
        var calendar = snapshot.Calendar;
        var findings = new List<MonitorFinding>();
        var tasksByModule = snapshot.Tasks.ToLookup(t => t.ModuleId);
        var childrenByParent = snapshot.Modules.Where(m => m.ParentId is not null).ToLookup(m => m.ParentId!.Value);

        foreach (var module in snapshot.Modules)
        {
            var overCapacity = ModuleOverCapacity(module, calendar);
            if (overCapacity is not null)
                findings.Add(overCapacity);
            else if (ModuleCapacityShortfall(module, calendar, today) is { } shortfall)
                findings.Add(shortfall);

            var subtreeTasks = SelfAndDescendants(module.Id, childrenByParent).SelectMany(id => tasksByModule[id]).ToList();

            var clocked = subtreeTasks.Sum(t => t.ClockedHours);
            if (module.AllocatedHours > 0 && clocked > module.AllocatedHours)
                findings.Add(new(MonitorRuleCodes.ModuleClockedOverAllocated, MonitorTargetTypes.Module, module.Id, module.Title, null,
                    $"{Hours(clocked)} h clocked against an allocation of {Hours(module.AllocatedHours)} h.",
                    Details(("clockedHours", clocked), ("allocatedHours", module.AllocatedHours))));

            var openTasks = subtreeTasks.Count(t => !t.IsComplete);
            if (!module.IsAchieved && module.EndDate < today && openTasks > 0)
                findings.Add(new(MonitorRuleCodes.ModuleOverdue, MonitorTargetTypes.Module, module.Id, module.Title, null,
                    $"The end date {Day(module.EndDate)} has passed with {openTasks} task(s) still open.",
                    Details(("endDate", Iso(module.EndDate)), ("openTasks", openTasks))));
        }

        foreach (var sprint in snapshot.Sprints)
        {
            if (sprint.Status != SprintStatuses.Active || sprint.EndDate is not { } end || end >= today)
                continue;
            var openTasks = snapshot.Tasks.Count(t => t.SprintId == sprint.Id && !t.IsComplete);
            if (openTasks > 0)
                findings.Add(new(MonitorRuleCodes.SprintOverdue, MonitorTargetTypes.Sprint, sprint.Id, sprint.Name, null,
                    $"The sprint ended {Day(end)} but is still open with {openTasks} unfinished task(s).",
                    Details(("endDate", Iso(end)), ("openTasks", openTasks))));
        }

        foreach (var task in snapshot.Tasks)
        {
            if (task.EstimatedHours is > 0 && task.ClockedHours > task.EstimatedHours)
                findings.Add(new(MonitorRuleCodes.TaskClockedOverEstimate, MonitorTargetTypes.Task, task.Id, task.Title, null,
                    $"{Hours(task.ClockedHours)} h clocked against an estimate of {Hours(task.EstimatedHours.Value)} h.",
                    Details(("clockedHours", task.ClockedHours), ("estimatedHours", task.EstimatedHours))));

            if (!task.IsComplete && task.DueDate is { } due && due < today)
                findings.Add(new(MonitorRuleCodes.TaskOverdue, MonitorTargetTypes.Task, task.Id, task.Title, null,
                    $"The due date {Day(due)} has passed and the task is not complete.",
                    Details(("dueDate", Iso(due)))));
        }

        foreach (var employeeId in snapshot.Tasks.SelectMany(t => t.AssigneeIds).Distinct())
            findings.AddRange(EmployeeDeadlineOverload(employeeId, snapshot.Tasks, calendar, today));

        return findings;
    }

    /// <summary>Planned too much: the allocation exceeds what the members can produce between the
    /// Module's start and end dates. A Module with no members counts its owner as one person.</summary>
    public static MonitorFinding? ModuleOverCapacity(MonitorModule module, WorkCalendar calendar)
    {
        var people = Math.Max(1, module.MemberCount);
        var capacity = calendar.Capacity(people, module.StartDate, module.EndDate);
        return module.AllocatedHours > capacity
            ? new(MonitorRuleCodes.ModuleOverCapacity, MonitorTargetTypes.Module, module.Id, module.Title, null,
                $"Allocated {Hours(module.AllocatedHours)} h, but {people} member(s) can produce at most {Hours(capacity)} h between {Day(module.StartDate)} and {Day(module.EndDate)}.",
                Details(("allocatedHours", module.AllocatedHours), ("capacityHours", capacity), ("memberCount", people)))
            : null;
    }

    /// <summary>Falling behind: the hours still to do exceed what the members can produce from today
    /// (or the start, if later) to the end date.</summary>
    public static MonitorFinding? ModuleCapacityShortfall(MonitorModule module, WorkCalendar calendar, DateOnly today)
    {
        if (module.IsAchieved || today > module.EndDate)
            return null;

        var people = Math.Max(1, module.MemberCount);
        var from = today > module.StartDate ? today : module.StartDate;
        var remaining = Math.Max(0m, module.AllocatedHours - module.CompletedHours);
        var capacity = calendar.Capacity(people, from, module.EndDate);
        return remaining > capacity
            ? new(MonitorRuleCodes.ModuleCapacityShortfall, MonitorTargetTypes.Module, module.Id, module.Title, null,
                $"{Hours(remaining)} h of work remain, but the team can produce only {Hours(capacity)} h before the {Day(module.EndDate)} deadline.",
                Details(("remainingHours", remaining), ("capacityHours", capacity), ("memberCount", people)))
            : null;
    }

    /// <summary>
    /// Earliest-deadline-first feasibility for one employee: for every due date d, the remaining hours
    /// of their open tasks due on or before d must fit in their working time from today to d. A shared
    /// task's remaining hours are split evenly between its assignees. Every task inside the first
    /// window that overflows (and later overflowing windows) is flagged once.
    /// </summary>
    public static IReadOnlyList<MonitorFinding> EmployeeDeadlineOverload(
        Guid employeeId, IReadOnlyList<MonitorTask> tasks, WorkCalendar calendar, DateOnly today)
    {
        var open = tasks
            .Where(t => !t.IsComplete && t.DueDate is { } due && due >= today
                && t.EstimatedHours is > 0 && t.AssigneeIds.Contains(employeeId))
            .Select(t => (Task: t, Due: t.DueDate!.Value,
                Remaining: Math.Max(0m, t.EstimatedHours!.Value - t.CompletedHours) / Math.Max(1, t.AssigneeIds.Count)))
            .OrderBy(x => x.Due)
            .ToList();

        var findings = new List<MonitorFinding>();
        var flagged = new HashSet<Guid>();
        foreach (var deadline in open.Select(x => x.Due).Distinct())
        {
            var inWindow = open.Where(x => x.Due <= deadline).ToList();
            var demand = inWindow.Sum(x => x.Remaining);
            var capacity = calendar.Capacity(1, today, deadline);
            if (demand <= capacity)
                continue;

            foreach (var (task, _, _) in inWindow.Where(x => flagged.Add(x.Task.Id)))
                findings.Add(new(MonitorRuleCodes.EmployeeDeadlineOverload, MonitorTargetTypes.Task, task.Id, task.Title, employeeId,
                    $"The assignee has {Hours(demand)} h of work due by {Day(deadline)} but only {Hours(capacity)} h of working time.",
                    Details(("employeeId", employeeId), ("dueDate", Iso(deadline)), ("demandHours", demand), ("capacityHours", capacity))));
        }
        return findings;
    }

    private static IEnumerable<Guid> SelfAndDescendants(Guid moduleId, ILookup<Guid, MonitorModule> childrenByParent)
    {
        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>([moduleId]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
                continue;
            yield return current;
            foreach (var child in childrenByParent[current])
                queue.Enqueue(child.Id);
        }
    }

    private static IReadOnlyDictionary<string, object?> Details(params (string Key, object? Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value);

    internal static string Hours(decimal hours) => hours.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Day(DateOnly date) => date.ToString("MMM d", CultureInfo.InvariantCulture);
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
