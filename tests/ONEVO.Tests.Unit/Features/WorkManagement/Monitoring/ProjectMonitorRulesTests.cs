using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class ProjectMonitorRulesTests
{
    // Mon 2026-10-05 .. Fri 2026-10-16 = 10 working days on the default 8h Mon-Fri calendar.
    private static readonly DateOnly Mon5 = new(2026, 10, 5);
    private static readonly DateOnly Wed7 = new(2026, 10, 7);
    private static readonly DateOnly Fri9 = new(2026, 10, 9);
    private static readonly DateOnly Mon12 = new(2026, 10, 12);
    private static readonly DateOnly Fri16 = new(2026, 10, 16);
    private static readonly DateOnly Fri2 = new(2026, 10, 2);
    private static readonly WorkCalendar Cal = WorkCalendar.Default;
    private static readonly Guid Emp = Guid.NewGuid();

    private static MonitorModule Module(decimal allocated, decimal completed = 0m, int members = 5,
        DateOnly? start = null, DateOnly? end = null, bool achieved = false, Guid? id = null, Guid? parentId = null)
        => new(id ?? Guid.NewGuid(), parentId, null, "Payments", start ?? Mon5, end ?? Fri16, allocated, completed, achieved, members);

    private static MonitorTask Task(DateOnly? due, decimal? estimate, Guid? moduleId = null, bool complete = false,
        decimal completed = 0m, decimal clocked = 0m, Guid? sprintId = null, params Guid[] assignees)
        => new(Guid.NewGuid(), moduleId ?? Guid.NewGuid(), null, sprintId, "Task", due, estimate, completed, complete, clocked,
            assignees.Length == 0 ? [Emp] : assignees);

    private static ProjectMonitorSnapshot Snapshot(IReadOnlyList<MonitorModule>? modules = null,
        IReadOnlyList<MonitorSprint>? sprints = null, IReadOnlyList<MonitorTask>? tasks = null)
        => new(Guid.NewGuid(), Cal, modules ?? [], sprints ?? [], tasks ?? []);

    // --- module capacity ---

    [Fact]
    public void ModuleOverCapacity_AllocatedAboveMembersTimesDaysTimesHours()
    {
        var finding = ProjectMonitorRules.ModuleOverCapacity(Module(allocated: 500m), Cal);

        Assert.NotNull(finding);
        Assert.Equal(MonitorRuleCodes.ModuleOverCapacity, finding!.RuleCode);
        Assert.Equal(400m, finding.Details["capacityHours"]);
    }

    [Fact]
    public void ModuleOverCapacity_ExactlyAtCapacity_None()
        => Assert.Null(ProjectMonitorRules.ModuleOverCapacity(Module(allocated: 400m), Cal));

    [Fact]
    public void ModuleOverCapacity_NoMembers_CountsOnePerson()
        => Assert.NotNull(ProjectMonitorRules.ModuleOverCapacity(Module(allocated: 81m, members: 0), Cal));

    [Fact]
    public void Shortfall_HalfwayWithNothingDone_ExactlyEnoughCapacity_None()
        // Mon 12 -> Fri 16 = 5 days x 5 people x 8h = 200h left; 200h still to do.
        => Assert.Null(ProjectMonitorRules.ModuleCapacityShortfall(Module(allocated: 200m), Cal, Mon12));

    [Fact]
    public void Shortfall_MoreWorkLeftThanTheTeamCanProduce_Flags()
    {
        var finding = ProjectMonitorRules.ModuleCapacityShortfall(Module(allocated: 260m, completed: 40m), Cal, Mon12);

        Assert.NotNull(finding);
        Assert.Equal(220m, finding!.Details["remainingHours"]);
        Assert.Equal(200m, finding.Details["capacityHours"]);
    }

    [Fact]
    public void Shortfall_AchievedOrPastEnd_None()
    {
        Assert.Null(ProjectMonitorRules.ModuleCapacityShortfall(Module(allocated: 999m, achieved: true), Cal, Mon12));
        Assert.Null(ProjectMonitorRules.ModuleCapacityShortfall(Module(allocated: 999m), Cal, Fri16.AddDays(1)));
    }

    [Fact]
    public void Evaluate_ReportsOverCapacityInsteadOfShortfall_ForTheSameModule()
    {
        var findings = ProjectMonitorRules.Evaluate(Snapshot(modules: [Module(allocated: 500m)]), Mon5);

        Assert.Contains(findings, f => f.RuleCode == MonitorRuleCodes.ModuleOverCapacity);
        Assert.DoesNotContain(findings, f => f.RuleCode == MonitorRuleCodes.ModuleCapacityShortfall);
    }

    // --- employee deadline overload (EDF) ---

    [Fact]
    public void Overload_WorkFitsBeforeEachDeadline_None()
        // Fri 9: 5 days x 8h = 40h capacity, 20h due.
        => Assert.Empty(ProjectMonitorRules.EmployeeDeadlineOverload(Emp, [Task(Fri9, 20m)], Cal, Mon5));

    [Fact]
    public void Overload_EarlierDeadlineOverflows_FlagsThatTaskAndLaterOnesInTheOverflowingWindow()
    {
        var a = Task(Fri9, 20m);   // demand by Fri 9 = 50 > 40
        var b = Task(Wed7, 30m);   // demand by Wed 7 = 30 > 24

        var findings = ProjectMonitorRules.EmployeeDeadlineOverload(Emp, [a, b], Cal, Mon5);

        Assert.Equal([b.Id, a.Id], findings.Select(f => f.TargetId));
        var first = findings[0];
        Assert.Equal(MonitorRuleCodes.EmployeeDeadlineOverload, first.RuleCode);
        Assert.Equal(Emp, first.SubjectEmployeeId);
        Assert.Equal(30m, first.Details["demandHours"]);
        Assert.Equal(24m, first.Details["capacityHours"]);
    }

    [Fact]
    public void Overload_SharedTaskSplitsItsHoursBetweenAssignees()
    {
        var other = Guid.NewGuid();
        var a = Task(Fri9, 20m);
        var b = Task(Wed7, 30m, assignees: [Emp, other]);   // 15h each -> 15 <= 24, 35 <= 40

        Assert.Empty(ProjectMonitorRules.EmployeeDeadlineOverload(Emp, [a, b], Cal, Mon5));
    }

    [Fact]
    public void Overload_UsesRemainingHours_AndIgnoresTasksThatCannotBePlanned()
    {
        var tasks = new[]
        {
            Task(Wed7, 30m, completed: 10m),       // 20 remaining <= 24
            Task(null, 100m),                      // no due date
            Task(Fri2, 100m),                      // already past due (task_overdue covers it)
            Task(Wed7, 100m, complete: true),      // done
            Task(Wed7, null),                      // no estimate
            Task(Wed7, 100m, assignees: [Guid.NewGuid()]) // someone else's
        };

        Assert.Empty(ProjectMonitorRules.EmployeeDeadlineOverload(Emp, tasks, Cal, Mon5));
    }

    // --- clocked hours ---

    [Fact]
    public void ClockedOverEstimate_Flags_AndNoEstimateIsIgnored()
    {
        var over = Task(Fri9, 5m, clocked: 6m);
        var noEstimate = Task(Fri9, null, clocked: 50m);

        var findings = ProjectMonitorRules.Evaluate(Snapshot(tasks: [over, noEstimate]), Mon5);

        Assert.Single(findings, f => f.RuleCode == MonitorRuleCodes.TaskClockedOverEstimate && f.TargetId == over.Id);
        Assert.DoesNotContain(findings, f => f.RuleCode == MonitorRuleCodes.TaskClockedOverEstimate && f.TargetId == noEstimate.Id);
    }

    [Fact]
    public void ModuleClockedOverAllocated_CountsTasksInChildModules()
    {
        var parent = Module(allocated: 10m, members: 5);
        var child = Module(allocated: 5m, parentId: parent.Id);
        var tasks = new[] { Task(Fri9, 4m, moduleId: parent.Id, clocked: 4m), Task(Fri9, 7m, moduleId: child.Id, clocked: 7m) };

        var findings = ProjectMonitorRules.Evaluate(Snapshot(modules: [parent, child], tasks: tasks), Mon5);

        Assert.Contains(findings, f => f.RuleCode == MonitorRuleCodes.ModuleClockedOverAllocated && f.TargetId == parent.Id);  // 11 > 10
        Assert.Contains(findings, f => f.RuleCode == MonitorRuleCodes.ModuleClockedOverAllocated && f.TargetId == child.Id);   // 7 > 5
    }

    // --- overdue ---

    [Fact]
    public void SprintOverdue_ActivePastEndWithOpenTask_Flags()
    {
        var sprint = new MonitorSprint(Guid.NewGuid(), null, "Sprint 4", SprintStatuses.Active, Fri2);
        var done = new MonitorSprint(Guid.NewGuid(), null, "Sprint 3", SprintStatuses.Complete, Fri2);
        var tasks = new[] { Task(Fri9, 1m, sprintId: sprint.Id), Task(Fri9, 1m, sprintId: done.Id) };

        var findings = ProjectMonitorRules.Evaluate(Snapshot(sprints: [sprint, done], tasks: tasks), Mon5);

        Assert.Single(findings, f => f.RuleCode == MonitorRuleCodes.SprintOverdue);
        Assert.Equal(sprint.Id, findings.Single(f => f.RuleCode == MonitorRuleCodes.SprintOverdue).TargetId);
    }

    [Fact]
    public void SprintOverdue_AllTasksDone_None()
    {
        var sprint = new MonitorSprint(Guid.NewGuid(), null, "Sprint 4", SprintStatuses.Active, Fri2);
        var findings = ProjectMonitorRules.Evaluate(Snapshot(sprints: [sprint], tasks: [Task(Fri2, 1m, sprintId: sprint.Id, complete: true)]), Mon5);

        Assert.DoesNotContain(findings, f => f.RuleCode == MonitorRuleCodes.SprintOverdue);
    }

    [Fact]
    public void TaskOverdue_PastDueAndOpen_Flags()
    {
        var late = Task(Fri2, 1m);
        var lateButDone = Task(Fri2, 1m, complete: true);

        var findings = ProjectMonitorRules.Evaluate(Snapshot(tasks: [late, lateButDone]), Mon5);

        Assert.Single(findings, f => f.RuleCode == MonitorRuleCodes.TaskOverdue);
        Assert.Equal(late.Id, findings.Single(f => f.RuleCode == MonitorRuleCodes.TaskOverdue).TargetId);
    }

    [Fact]
    public void ModuleOverdue_PastEndWithAnOpenTaskInAChildModule_Flags()
    {
        var parent = Module(allocated: 0m, end: Fri2);
        var child = Module(allocated: 0m, end: Fri2, parentId: parent.Id);
        var tasks = new[] { Task(null, null, moduleId: child.Id) };

        var findings = ProjectMonitorRules.Evaluate(Snapshot(modules: [parent, child], tasks: tasks), Mon5);

        Assert.Contains(findings, f => f.RuleCode == MonitorRuleCodes.ModuleOverdue && f.TargetId == parent.Id);
        Assert.Contains(findings, f => f.RuleCode == MonitorRuleCodes.ModuleOverdue && f.TargetId == child.Id);
    }

    [Fact]
    public void ModuleOverdue_AchievedOrNoOpenTasks_None()
    {
        var achieved = Module(allocated: 0m, end: Fri2, achieved: true);
        var empty = Module(allocated: 0m, end: Fri2);
        var tasks = new[] { Task(null, null, moduleId: achieved.Id) };

        var findings = ProjectMonitorRules.Evaluate(Snapshot(modules: [achieved, empty], tasks: tasks), Mon5);

        Assert.DoesNotContain(findings, f => f.RuleCode == MonitorRuleCodes.ModuleOverdue);
    }
}
