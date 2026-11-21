namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

public static class MonitorRuleCodes
{
    public const string ModuleOverCapacity = "module_over_capacity";
    public const string ModuleCapacityShortfall = "module_capacity_shortfall";
    public const string EmployeeDeadlineOverload = "employee_deadline_overload";
    public const string TaskClockedOverEstimate = "task_clocked_over_estimate";
    public const string ModuleClockedOverAllocated = "module_clocked_over_allocated";
    public const string SprintOverdue = "sprint_overdue";
    public const string TaskOverdue = "task_overdue";
    public const string ModuleOverdue = "module_overdue";
}

public static class MonitorTargetTypes
{
    public const string Module = "module";
    public const string Sprint = "sprint";
    public const string Task = "task";
}

/// <summary>A Module as the monitor sees it. MemberCount = its manpower: distinct active members and owners of
/// the Module and all its sub-Modules (see ModuleManpower).
/// CompletedHours already includes child Modules (it is rolled up when tasks complete).
/// ConversionRate = the pooled 4-week task rate of that manpower (null = no history: the rules then assume 100%).
/// RatePoolSize = how many people with worked time that rate is pooled over; module alerts are visible to every module member, so the
/// rate is printed only for pools of at least ProjectMonitorRules.MinRatePool people.</summary>
public sealed record MonitorModule(
    Guid Id, Guid? ParentId, Guid? CreatorPositionModuleId, string Title, DateOnly StartDate, DateOnly EndDate,
    decimal AllocatedHours, decimal CompletedHours, bool IsAchieved, int MemberCount, decimal? ConversionRate = null,
    int RatePoolSize = 0);

public sealed record MonitorSprint(Guid Id, Guid? CreatorPositionModuleId, string Name, string Status, DateOnly? EndDate);

/// <summary>A task (or subtask). IsComplete = its status marks tasks complete; ClockedHours = closed
/// clocking sessions.</summary>
public sealed record MonitorTask(
    Guid Id, Guid ModuleId, Guid? CreatorPositionModuleId, Guid? SprintId, string Title, DateOnly? DueDate,
    decimal? EstimatedHours, decimal CompletedHours, bool IsComplete, decimal ClockedHours, IReadOnlyList<Guid> AssigneeIds);

/// <summary>Everything the monitor rules read about one project, loaded once. Rates = the 4-week conversion rate of
/// every member, owner and assignee (null = none known: every rule assumes 100%). MaxTaskRate = the overload threshold
/// of the project's owning Legal Entity (wm_task_rate_settings, read once per evaluation).</summary>
public sealed record ProjectMonitorSnapshot(
    Guid ProjectId, WorkCalendar Calendar,
    IReadOnlyList<MonitorModule> Modules, IReadOnlyList<MonitorSprint> Sprints, IReadOnlyList<MonitorTask> Tasks,
    ConversionRateSet? Rates = null, decimal MaxTaskRate = TaskRateLimit.Default);

/// <summary>Only what MonitorAlertRecipients.CreatorPosition reads about a project's Modules, sprints and tasks,
/// with the snapshot's own filters. Lets the Inbox find alert recipients without loading the whole snapshot.</summary>
public sealed record MonitorTargets(
    IEnumerable<MonitorTargetModule> Modules, IEnumerable<MonitorTargetSprint> Sprints, IEnumerable<MonitorTargetTask> Tasks)
{
    /// <summary>A lazy view over the snapshot, so a lookup costs what it did on the snapshot itself.</summary>
    public static MonitorTargets From(ProjectMonitorSnapshot snapshot) => new(
        snapshot.Modules.Select(m => new MonitorTargetModule(m.Id, m.ParentId, m.CreatorPositionModuleId)),
        snapshot.Sprints.Select(s => new MonitorTargetSprint(s.Id, s.CreatorPositionModuleId)),
        snapshot.Tasks.Select(t => new MonitorTargetTask(t.Id, t.ModuleId, t.CreatorPositionModuleId)));
}

public sealed record MonitorTargetModule(Guid Id, Guid? ParentId, Guid? CreatorPositionModuleId);

public sealed record MonitorTargetSprint(Guid Id, Guid? CreatorPositionModuleId);

public sealed record MonitorTargetTask(Guid Id, Guid ModuleId, Guid? CreatorPositionModuleId);

/// <summary>One detected problem. (TargetId, RuleCode, SubjectEmployeeId) identifies it across runs.</summary>
public sealed record MonitorFinding(
    string RuleCode, string TargetType, Guid TargetId, string TargetTitle, Guid? SubjectEmployeeId,
    string Message, IReadOnlyDictionary<string, object?> Details);
