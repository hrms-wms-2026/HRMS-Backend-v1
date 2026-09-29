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

/// <summary>A Module as the monitor sees it. MemberCount = distinct active members plus the owner.
/// CompletedHours already includes child Modules (it is rolled up when tasks complete).</summary>
public sealed record MonitorModule(
    Guid Id, Guid? ParentId, Guid? CreatorPositionModuleId, string Title, DateOnly StartDate, DateOnly EndDate,
    decimal AllocatedHours, decimal CompletedHours, bool IsAchieved, int MemberCount);

public sealed record MonitorSprint(Guid Id, Guid? CreatorPositionModuleId, string Name, string Status, DateOnly? EndDate);

/// <summary>A task (or subtask). IsComplete = its status marks tasks complete; ClockedHours = closed
/// clocking sessions.</summary>
public sealed record MonitorTask(
    Guid Id, Guid ModuleId, Guid? CreatorPositionModuleId, Guid? SprintId, string Title, DateOnly? DueDate,
    decimal? EstimatedHours, decimal CompletedHours, bool IsComplete, decimal ClockedHours, IReadOnlyList<Guid> AssigneeIds);

/// <summary>Everything the monitor rules read about one project, loaded once.</summary>
public sealed record ProjectMonitorSnapshot(
    Guid ProjectId, WorkCalendar Calendar,
    IReadOnlyList<MonitorModule> Modules, IReadOnlyList<MonitorSprint> Sprints, IReadOnlyList<MonitorTask> Tasks);

/// <summary>One detected problem. (TargetId, RuleCode, SubjectEmployeeId) identifies it across runs.</summary>
public sealed record MonitorFinding(
    string RuleCode, string TargetType, Guid TargetId, string TargetTitle, Guid? SubjectEmployeeId,
    string Message, IReadOnlyDictionary<string, object?> Details);
