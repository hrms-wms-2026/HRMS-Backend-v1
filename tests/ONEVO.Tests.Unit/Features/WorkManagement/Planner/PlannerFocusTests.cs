using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Planner = Calendar (2026-10-27 design, section 4.2, call C4): the Focus list's groups, numbers and order.</summary>
public class PlannerFocusTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static PlannerFocusTaskRow Row(string title, DateOnly? due = null, decimal? estimateHours = null, int logged = 0,
        string priority = "medium", Guid? id = null)
        => new(id ?? Guid.NewGuid(), Guid.NewGuid(), "Portal", title, priority, due, estimateHours, logged);

    private static PlannedTaskBlock Block(Guid taskId, DateOnly day, int startHour, int minutes)
    {
        var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(startHour, 0)), TimeSpan.FromHours(5.5));
        return new PlannedTaskBlock { Id = Guid.NewGuid(), TaskId = taskId, StartAt = start, EndAt = start.AddMinutes(minutes) };
    }

    private static PlannerFocusGroup Group(PlannerFocusResponse r, string reason) => r.Groups.Single(g => g.Reason == reason);

    [Fact]
    public void Tasks_land_in_the_four_groups_by_due_date()
    {
        var r = PlannerFocus.Build(
            [Row("late", new(2026, 10, 30)), Row("today", Monday), Row("sunday", new(2026, 11, 8)), Row("next monday", new(2026, 11, 9)), Row("no date")],
            [], Monday, Colombo);

        Assert.Equal(["overdue", "due_today", "due_this_week", "backlog"], r.Groups.Select(g => g.Reason));
        Assert.Equal((Monday, new DateOnly(2026, 11, 8)), (r.Today, r.WeekEnd));
        Assert.Equal(["late"], Group(r, PlannerFocusReasons.Overdue).Items.Select(i => i.Title));
        Assert.Equal(["today"], Group(r, PlannerFocusReasons.DueToday).Items.Select(i => i.Title));
        Assert.Equal(["sunday"], Group(r, PlannerFocusReasons.DueThisWeek).Items.Select(i => i.Title));
        Assert.Equal(["next monday", "no date"], Group(r, PlannerFocusReasons.Backlog).Items.Select(i => i.Title));
    }

    [Fact]
    public void Remaining_is_estimate_less_logged_and_null_without_estimate()
    {
        var r = PlannerFocus.Build([Row("a", estimateHours: 5, logged: 120), Row("b", estimateHours: 1, logged: 90), Row("c", logged: 30)],
            [], Monday, Colombo);
        var items = Group(r, PlannerFocusReasons.Backlog).Items.ToDictionary(i => i.Title);

        Assert.Equal((300, 120, 180), (items["a"].EstimatedMinutes, items["a"].LoggedMinutes, items["a"].RemainingMinutes));
        Assert.Equal(0, items["b"].RemainingMinutes);
        Assert.Equal(((int?)null, (int?)null), (items["c"].EstimatedMinutes, items["c"].RemainingMinutes));
    }

    [Fact]
    public void Blocks_from_today_on_set_planned_ahead_first_day_and_day_count()
    {
        var planned = Guid.NewGuid();
        var short_ = Guid.NewGuid();
        var r = PlannerFocus.Build(
            [Row("planned", estimateHours: 2.5m, id: planned), Row("short", estimateHours: 3, id: short_)],
            [Block(planned, Monday.AddDays(3), 9, 90), Block(planned, Monday.AddDays(1), 9, 60), Block(short_, Monday.AddDays(1), 14, 150)],
            Monday, Colombo);
        var items = Group(r, PlannerFocusReasons.Backlog).Items.ToDictionary(i => i.Title);

        Assert.Equal((150, new DateOnly(2026, 11, 3), 2, true),
            (items["planned"].PlannedAheadMinutes, items["planned"].FirstPlannedDate, items["planned"].PlannedDayCount, items["planned"].FullyPlanned));
        Assert.Equal((150, false), (items["short"].PlannedAheadMinutes, items["short"].FullyPlanned));
    }

    [Fact]
    public void A_task_without_estimate_is_never_fully_planned()
    {
        var id = Guid.NewGuid();
        var item = Group(PlannerFocus.Build([Row("x", id: id)], [Block(id, Monday, 9, 60)], Monday, Colombo), PlannerFocusReasons.Backlog).Items.Single();

        Assert.Equal((60, false), (item.PlannedAheadMinutes, item.FullyPlanned));
    }

    [Fact]
    public void Sorted_fully_planned_last_then_priority_then_due_then_title()
    {
        var done = Guid.NewGuid();
        var r = PlannerFocus.Build(
        [
            Row("fully planned critical", priority: "critical", estimateHours: 1, id: done),
            Row("b low", priority: "low"),
            Row("a high later", new(2026, 11, 20), priority: "high"),
            Row("z high sooner", new(2026, 11, 12), priority: "high"),
            Row("A medium", priority: "medium"),
            Row("b medium", priority: "medium"),
            Row("critical", priority: "critical"),
        ], [Block(done, Monday, 9, 60)], Monday, Colombo);

        Assert.Equal(["critical", "z high sooner", "a high later", "A medium", "b medium", "b low", "fully planned critical"],
            Group(r, PlannerFocusReasons.Backlog).Items.Select(i => i.Title));
    }

    [Fact]
    public void Each_group_keeps_fifty_items_and_the_full_count()
    {
        var r = PlannerFocus.Build(Enumerable.Range(0, 60).Select(i => Row($"t{i:00}")).ToList(), [], Monday, Colombo);
        var backlog = Group(r, PlannerFocusReasons.Backlog);

        Assert.Equal((60, PlannerFocus.MaxItemsPerGroup), (backlog.Count, backlog.Items.Count));
        Assert.Equal(0, Group(r, PlannerFocusReasons.Overdue).Count);
    }

    [Fact]
    public void On_Sunday_night_a_Monday_task_is_backlog_and_becomes_due_today_on_Monday()
    {
        var sunday = new DateOnly(2026, 11, 8);
        var row = Row("monday", new(2026, 11, 9));

        Assert.Equal(PlannerFocusReasons.Backlog, PlannerFocus.Build([row], [], sunday, Colombo).Groups.Single(g => g.Count == 1).Reason);
        Assert.Equal(PlannerFocusReasons.DueToday, PlannerFocus.Build([row], [], sunday.AddDays(1), Colombo).Groups.Single(g => g.Count == 1).Reason);
    }

    [Fact]
    public void Items_carry_their_category_kind_and_created_date()
    {
        var created = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
        var row = new PlannerFocusTaskRow(Guid.NewGuid(), Guid.NewGuid(), "Portal", "Fix login", "high", null, 1, 0,
            CategoryName: "Bugs", CategoryKind: "bug", CreatedAt: created);

        var item = PlannerFocus.Build([row], [], Monday, Colombo).Groups.SelectMany(g => g.Items).Single();

        Assert.Equal(("Bugs", "bug", created), (item.CategoryName, item.CategoryKind, item.CreatedAt));
    }

    [Fact]
    public void A_task_with_no_category_is_a_task_with_an_empty_name()
    {
        var item = PlannerFocus.Build([Row("plain")], [], Monday, Colombo).Groups.SelectMany(g => g.Items).Single();

        Assert.Equal(("", "task"), (item.CategoryName, item.CategoryKind));
    }
}
