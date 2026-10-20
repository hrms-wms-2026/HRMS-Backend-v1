using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

public sealed class LedHourRollupTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 20);

    private static LedObjectiveRow Row(Guid id, Guid? parent, string title, decimal allocated)
        => new(id, Project, parent, title, false, new DateOnly(2026, 12, 31), allocated);

    private static ModuleHourSummary Summary(Guid id, decimal completed, decimal inProgress, decimal notStarted, int tasks,
        int unestimated = 0, DateOnly? earliestOpenDue = null) => new()
    {
        Id = id, ProjectId = Project, CompletedHours = completed, InProgressHours = inProgress, NotStartedHours = notStarted,
        TaskCount = tasks, UnestimatedTaskCount = unestimated, EarliestOpenDueDate = earliestOpenDue,
    };

    private static LedHeadModule Head(LedObjectiveRow head, params LedObjectiveRow[] descendants)
    {
        var rows = new[] { head }.Concat(descendants).ToList();
        return new LedHeadModule(head.Id, Project, head.Title, false, head.EndDate, rows.Select(r => r.Id).ToHashSet(),
            head.AllocatedHours, rows);
    }

    [Fact]
    public void Untasked_hours_are_the_allocation_minus_the_whole_subtree_task_hours()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var head = Head(Row(a, null, "A", 200), Row(b, a, "B", 80), Row(c, b, "C", 30));
        var summaries = new Dictionary<Guid, ModuleHourSummary>
        {
            [a] = Summary(a, completed: 40, inProgress: 10, notStarted: 20, tasks: 5),
            [b] = Summary(b, completed: 0, inProgress: 20, notStarted: 30, tasks: 3, unestimated: 1),
            [c] = Summary(c, completed: 0, inProgress: 0, notStarted: 20, tasks: 2),
        };

        var modules = LedHourRollup.ForHead(head, summaries, Today);

        Assert.Equal(new[] { a, b, c }, modules.Select(m => m.ObjectiveId));
        Assert.Equal(new[] { 0, 1, 2 }, modules.Select(m => m.Depth));
        Assert.Equal(new LedHourTotals(200, 40, 30, 70, 60, 0, 10, 1), modules[0].Hours);
        Assert.Equal(new LedHourTotals(80, 0, 20, 50, 10, 0, 5, 1), modules[1].Hours);
        Assert.Equal(new LedHourTotals(30, 0, 0, 20, 10, 0, 2, 0), modules[2].Hours);
        Assert.Null(modules[0].ParentObjectiveId);
        Assert.Equal(b, modules[2].ParentObjectiveId);
    }

    [Fact]
    public void Tasks_beyond_the_allocation_show_as_over_tasked_not_negative_untasked()
    {
        var a = Guid.NewGuid();
        var head = Head(Row(a, null, "A", 10));
        var summaries = new Dictionary<Guid, ModuleHourSummary> { [a] = Summary(a, 5, 5, 4, 3) };

        var hours = LedHourRollup.ForHead(head, summaries, Today)[0].Hours;

        Assert.Equal(0, hours.UntaskedHours);
        Assert.Equal(4, hours.OverTaskedHours);
    }

    [Fact]
    public void A_module_without_a_summary_row_counts_as_no_tasks_and_overdue_work_marks_its_ancestors_at_risk()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var head = Head(Row(a, null, "A", 50), Row(b, a, "B", 10), Row(c, a, "C", 10));
        var summaries = new Dictionary<Guid, ModuleHourSummary>
        {
            [c] = Summary(c, 0, 0, 6, 1, earliestOpenDue: Today.AddDays(-1)),
        };

        var modules = LedHourRollup.ForHead(head, summaries, Today);

        Assert.Equal(new LedHourTotals(10, 0, 0, 0, 10, 0, 0, 0), modules.Single(m => m.ObjectiveId == b).Hours);
        Assert.True(modules.Single(m => m.ObjectiveId == a).AtRisk);
        Assert.False(modules.Single(m => m.ObjectiveId == b).AtRisk);
        Assert.True(modules.Single(m => m.ObjectiveId == c).AtRisk);
    }

    [Fact]
    public void Work_due_today_is_not_at_risk()
    {
        var a = Guid.NewGuid();
        var summaries = new Dictionary<Guid, ModuleHourSummary> { [a] = Summary(a, 0, 1, 0, 1, earliestOpenDue: Today) };

        Assert.False(LedHourRollup.ForHead(Head(Row(a, null, "A", 5)), summaries, Today)[0].AtRisk);
    }
}
