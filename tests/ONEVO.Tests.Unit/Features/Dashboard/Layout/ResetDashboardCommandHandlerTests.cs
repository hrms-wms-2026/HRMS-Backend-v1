using ONEVO.Application.Features.Dashboard.Layout.Commands.ResetDashboard;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class ResetDashboardCommandHandlerTests
{
    private readonly DashboardMutationFixture _f = new();

    private ResetDashboardCommandHandler Handler() => new(_f.Repo.Object, _f.User.Object);

    [Fact]
    public async Task Restores_full_default_including_not_allowed_widgets()
    {
        var d = _f.Seed(DashboardKeys.MyWork, version: 4);
        var actionCenter = d.Widgets.Single(w => w.WidgetKey == "action-center");
        var tasks = d.Widgets.Single(w => w.WidgetKey == "my-tasks");
        var tasksId = tasks.Id;
        // Customise: scramble a layout, drop a default widget, add a non-default one with a layout.
        foreach (var l in tasks.Layouts) { l.X = 5; l.Y = 50; l.W = 3; l.H = 3; }
        tasks.SettingsJson = "{\"x\":1}";
        d.Widgets.Remove(d.Widgets.Single(w => w.WidgetKey == "wellbeing" || w.WidgetKey == "work-pattern"));
        var extra = new ONEVO.Domain.Features.Dashboard.Entities.DashboardWidget
        {
            Id = Guid.NewGuid(), TenantId = DashboardMutationFixture.TenantId, DashboardId = d.Id, WidgetKey = "notifications",
            Layouts = [new() { Id = Guid.NewGuid(), TenantId = DashboardMutationFixture.TenantId, Breakpoint = "desktop", X = 0, Y = 30, W = 3, H = 6 }],
        };
        extra.Layouts[0].WidgetId = extra.Id;
        d.Widgets.Add(extra);

        var result = await Handler().Handle(new ResetDashboardCommand(d.Id, 4), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);

        var defaults = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, Breakpoints.Desktop);
        Assert.Equal(defaults.Select(x => x.Key).Order(), d.Widgets.Select(w => w.WidgetKey).Order());
        Assert.Contains(d.Widgets, w => w.Id == actionCenter.Id); // not-allowed widget kept (full default)
        Assert.Contains(d.Widgets, w => w.Id == tasksId);          // existing row reused, not re-created
        Assert.Equal("{}", tasks.SettingsJson);
        Assert.DoesNotContain(d.Widgets, w => w.WidgetKey == "notifications");
        Assert.Empty(extra.Layouts);

        foreach (var bp in Breakpoints.All)
        {
            foreach (var (key, rect) in DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, bp))
            {
                var w = d.Widgets.Single(x => x.WidgetKey == key);
                Assert.Equal(rect, DashboardMutationFixture.Rect(w, bp));
            }
        }
    }

    [Fact]
    public async Task Missing_default_breakpoint_row_is_added_on_existing_widget()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var w = d.Widgets.Single(x => x.WidgetKey == "my-tasks");
        w.Layouts.RemoveAll(l => l.Breakpoint == "tablet");

        var result = await Handler().Handle(new ResetDashboardCommand(d.Id, 1), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, w.Layouts.Count);
    }

    [Fact]
    public async Task Stale_version_conflicts()
    {
        var d = _f.Seed(DashboardKeys.MyWork, version: 9);

        var result = await Handler().Handle(new ResetDashboardCommand(d.Id, 8), default);

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("currentVersion=9", result.Error);
    }

    [Fact]
    public async Task Foreign_dashboard_not_found()
    {
        var d = _f.Seed(DashboardKeys.MyWork, owner: Guid.NewGuid());

        var result = await Handler().Handle(new ResetDashboardCommand(d.Id, 1), default);

        Assert.Equal(404, result.StatusCode);
    }
}
