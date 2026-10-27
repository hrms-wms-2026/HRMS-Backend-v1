using ONEVO.Application.Features.Dashboard.Layout.Commands.RemoveDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class RemoveDashboardWidgetCommandHandlerTests
{
    private readonly DashboardMutationFixture _f = new();

    private RemoveDashboardWidgetCommandHandler Handler() => new(_f.Repo.Object, _f.Access.Object, _f.User.Object);

    [Fact]
    public async Task Removes_widget_and_its_layouts_and_bumps_version()
    {
        var d = _f.Seed(DashboardKeys.MyWork, version: 2);
        var target = d.Widgets.Single(w => w.WidgetKey == "my-tasks");
        var layouts = target.Layouts.ToList();

        var result = await Handler().Handle(new RemoveDashboardWidgetCommand(d.Id, target.Id, 2), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        Assert.DoesNotContain(d.Widgets, w => w.Id == target.Id);
        Assert.Empty(target.Layouts);
        Assert.Equal(3, layouts.Count);
    }

    [Fact]
    public async Task Remaining_visible_widgets_are_gravity_compacted()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var target = d.Widgets.Single(w => w.WidgetKey == "todays-schedule");

        await Handler().Handle(new RemoveDashboardWidgetCommand(d.Id, target.Id, 1), default);

        // work-pattern (12 wide, y=12) floats up; my-tasks (0,8,6,4) floats to y=0+.
        var visible = d.Widgets.Where(w => w.WidgetKey != "action-center").ToList();
        foreach (var bp in Breakpoints.All)
        {
            var items = visible.Select(w => (w.WidgetKey, DashboardMutationFixture.Rect(w, bp))).ToList();
            Assert.Empty(DashboardGrid.Validate(items, bp));
            Assert.Equal(items.OrderBy(i => i.Item2.Y).ThenBy(i => i.Item2.X).Select(i => i.Item2).ToList(),
                DashboardGrid.Compact(items).Select(i => i.Rect).OrderBy(r => r.Y).ThenBy(r => r.X).ToList());
        }

        Assert.True(DashboardMutationFixture.Rect(d.Widgets.Single(w => w.WidgetKey == "my-tasks"), "desktop").Y < 8);
    }

    [Fact]
    public async Task Hidden_widget_layouts_are_not_changed()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var hidden = d.Widgets.Single(w => w.WidgetKey == "action-center");
        var before = DashboardMutationFixture.Rect(hidden, "desktop");
        var target = d.Widgets.Single(w => w.WidgetKey == "today-at-a-glance");

        await Handler().Handle(new RemoveDashboardWidgetCommand(d.Id, target.Id, 1), default);

        Assert.Equal(before, DashboardMutationFixture.Rect(hidden, "desktop"));
    }

    [Fact]
    public async Task Hidden_widget_cannot_be_removed()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var hidden = d.Widgets.Single(w => w.WidgetKey == "action-center");

        var result = await Handler().Handle(new RemoveDashboardWidgetCommand(d.Id, hidden.Id, 1), default);

        Assert.Equal(404, result.StatusCode);
        Assert.Contains(d.Widgets, w => w.Id == hidden.Id);
    }

    [Fact]
    public async Task Stale_version_conflicts()
    {
        var d = _f.Seed(DashboardKeys.MyWork, version: 4);
        var target = d.Widgets.First();

        var result = await Handler().Handle(new RemoveDashboardWidgetCommand(d.Id, target.Id, 3), default);

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("currentVersion=4", result.Error);
        Assert.Contains(d.Widgets, w => w.Id == target.Id);
    }

    [Fact]
    public async Task Foreign_dashboard_not_found()
    {
        var d = _f.Seed(DashboardKeys.MyWork, owner: Guid.NewGuid());

        var result = await Handler().Handle(new RemoveDashboardWidgetCommand(d.Id, d.Widgets[0].Id, 1), default);

        Assert.Equal(404, result.StatusCode);
    }
}
