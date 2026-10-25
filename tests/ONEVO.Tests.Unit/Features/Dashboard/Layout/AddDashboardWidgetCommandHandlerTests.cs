using Moq;
using ONEVO.Application.Features.Dashboard.Layout.Commands.AddDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class AddDashboardWidgetCommandHandlerTests
{
    private readonly DashboardMutationFixture _f = new();

    private AddDashboardWidgetCommandHandler Handler() => new(_f.Repo.Object, _f.Access.Object, _f.User.Object);

    // notifications / wellbeing are allowed catalog widgets that are not placed by default.
    private static readonly string[] Allowed = [.. DashboardMutationFixture.PlainEmployeeWork, "notifications", "wellbeing"];

    [Fact]
    public async Task Adds_widget_and_bumps_version()
    {
        var d = _f.Seed(DashboardKeys.MyWork, version: 3, allowed: Allowed);

        var result = await Handler().Handle(new AddDashboardWidgetCommand(d.Id, "notifications", 3, null), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.LayoutVersion);
        Assert.Contains(d.Widgets, w => w.Id == result.Value.InstanceId && w.WidgetKey == "notifications");
    }

    [Fact]
    public async Task Stale_version_conflicts()
    {
        var d = _f.Seed(DashboardKeys.MyWork, version: 5, allowed: Allowed);

        var result = await Handler().Handle(new AddDashboardWidgetCommand(d.Id, "notifications", 4, null), default);

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("LAYOUT_VERSION_CONFLICT", result.Error);
        Assert.Contains("currentVersion=5", result.Error);
        Assert.DoesNotContain(d.Widgets, w => w.WidgetKey == "notifications");
    }

    [Fact]
    public async Task Lost_race_conflicts_with_stored_version()
    {
        var d = _f.Seed(DashboardKeys.MyWork, version: 2, allowed: Allowed);
        _f.Repo.Setup(r => r.SaveWithVersionAsync(d, 2, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Handler().Handle(new AddDashboardWidgetCommand(d.Id, "notifications", 2, null), default);

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("LAYOUT_VERSION_CONFLICT", result.Error);
    }

    [Fact]
    public async Task Foreign_dashboard_not_found()
    {
        var d = _f.Seed(DashboardKeys.MyWork, owner: Guid.NewGuid(), allowed: Allowed);

        var result = await Handler().Handle(new AddDashboardWidgetCommand(d.Id, "notifications", 1, null), default);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Team_widget_on_my_work_forbidden()
    {
        var d = _f.Seed(DashboardKeys.MyWork, allowed: Allowed);

        var result = await Handler().Handle(new AddDashboardWidgetCommand(d.Id, "team-status", 1, null), default);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Not_allowed_widget_forbidden()
    {
        var d = _f.Seed(DashboardKeys.MyWork);

        var result = await Handler().Handle(new AddDashboardWidgetCommand(d.Id, "action-center", 1, null), default);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Duplicate_conflicts()
    {
        var d = _f.Seed(DashboardKeys.MyWork, allowed: Allowed);

        var result = await Handler().Handle(new AddDashboardWidgetCommand(d.Id, "my-tasks", 1, null), default);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("WIDGET_ALREADY_PLACED", result.Error);
    }

    [Fact]
    public async Task New_widget_placed_on_all_breakpoints_without_overlap()
    {
        var d = _f.Seed(DashboardKeys.MyWork, allowed: Allowed);

        var result = await Handler().Handle(new AddDashboardWidgetCommand(d.Id, "notifications", 1, null), default);

        Assert.True(result.IsSuccess);
        var added = d.Widgets.Single(w => w.WidgetKey == "notifications");
        var (dw, dh) = DashboardWidgetRegistry.DefaultSize("notifications");
        Assert.Equal(3, added.Layouts.Count);
        var desktop = DashboardMutationFixture.Rect(added, "desktop");
        Assert.Equal((dw, dh), (desktop.W, desktop.H));
        Assert.Equal(4, DashboardMutationFixture.Rect(added, "mobile").W);

        var visible = d.Widgets.Where(w => w.WidgetKey != "action-center").ToList();
        foreach (var bp in Breakpoints.All)
        {
            var items = visible.Select(w => (w.WidgetKey, DashboardMutationFixture.Rect(w, bp))).ToList();
            Assert.Empty(DashboardGrid.Validate(items, bp));
        }
    }

    [Fact]
    public async Task Valid_desktop_rect_is_used_when_free_and_compacted()
    {
        var d = _f.Seed(DashboardKeys.MyWork, allowed: Allowed);
        // Rows 16+ are empty in the default layout; compaction floats the widget up to the free space.
        var result = await Handler().Handle(
            new AddDashboardWidgetCommand(d.Id, "notifications", 1, new GridRect(0, 20, 3, 6)), default);

        Assert.True(result.IsSuccess);
        var rect = DashboardMutationFixture.Rect(d.Widgets.Single(w => w.WidgetKey == "notifications"), "desktop");
        Assert.Equal(3, rect.W);
        Assert.Equal(6, rect.H);
        Assert.True(rect.Y <= 20);
    }

    [Fact]
    public async Task Invalid_desktop_rect_falls_back_to_default_size_auto_place()
    {
        var d = _f.Seed(DashboardKeys.MyWork, allowed: Allowed);

        // Overlaps today-at-a-glance at (0,0).
        var result = await Handler().Handle(
            new AddDashboardWidgetCommand(d.Id, "notifications", 1, new GridRect(0, 0, 3, 6)), default);

        Assert.True(result.IsSuccess);
        var (dw, dh) = DashboardWidgetRegistry.DefaultSize("notifications");
        var rect = DashboardMutationFixture.Rect(d.Widgets.Single(w => w.WidgetKey == "notifications"), "desktop");
        Assert.Equal((dw, dh), (rect.W, rect.H));
        Assert.DoesNotContain(d.Widgets.Where(w => w.WidgetKey != "notifications" && w.WidgetKey != "action-center"),
            w => DashboardGrid.Overlaps(DashboardMutationFixture.Rect(w, "desktop"), rect));
    }
}
