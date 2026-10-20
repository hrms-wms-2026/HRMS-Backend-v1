using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class DashboardWidgetRegistryTests
{
    private static readonly string[] Dashboards = [DashboardKeys.MyWork, DashboardKeys.MyTeam];

    [Fact]
    public void Catalogs_are_disjoint_and_every_key_belongs_to_exactly_one_dashboard()
    {
        var work = DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyWork).Select(d => d.Key).ToList();
        var team = DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyTeam).Select(d => d.Key).ToList();

        Assert.Equal(10, work.Count);
        Assert.Equal(3, team.Count);
        Assert.Empty(work.Intersect(team));
        Assert.Equal(work.Count + team.Count, work.Concat(team).Distinct().Count());
        foreach (var key in work.Concat(team))
        {
            Assert.Equal(
                1,
                Dashboards.Count(d => DashboardWidgetRegistry.ForDashboard(d).Any(w => w.Key == key)));
        }
    }

    [Fact]
    public void Default_layouts_are_valid_for_every_dashboard_and_breakpoint()
    {
        foreach (var dashboard in Dashboards)
        {
            foreach (var breakpoint in Breakpoints.All)
            {
                var layout = DashboardWidgetRegistry.DefaultLayout(dashboard, breakpoint);

                Assert.NotEmpty(layout);
                Assert.Empty(DashboardGrid.Validate(layout, breakpoint));
            }
        }
    }

    [Fact]
    public void Default_rects_respect_each_definitions_min_and_max()
    {
        foreach (var dashboard in Dashboards)
        {
            foreach (var (key, rect) in DashboardWidgetRegistry.DefaultLayout(dashboard, Breakpoints.Desktop))
            {
                var def = DashboardWidgetRegistry.Get(key)!;

                Assert.InRange(rect.W, def.MinW, def.MaxW);
                Assert.InRange(rect.H, def.MinH, def.MaxH);
            }
        }
    }

    [Fact]
    public void Mobile_derived_layout_is_single_column_in_desktop_reading_order()
    {
        var mobile = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, Breakpoints.Mobile);

        Assert.Equal(
            ["today-at-a-glance", "action-center", "quick-actions", "todays-schedule", "today-total-time", "active-projects", "my-tasks", "work-pattern"],
            mobile.Select(i => i.Key).ToArray());
        Assert.All(mobile, i =>
        {
            Assert.Equal(0, i.Rect.X);
            Assert.Equal(4, i.Rect.W);
        });
    }

    [Fact]
    public void Notifications_and_wellbeing_are_in_catalog_but_not_in_default_layout()
    {
        var keys = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, Breakpoints.Desktop).Select(i => i.Key).ToList();

        Assert.NotNull(DashboardWidgetRegistry.Get("notifications"));
        Assert.NotNull(DashboardWidgetRegistry.Get("wellbeing"));
        Assert.DoesNotContain("notifications", keys);
        Assert.DoesNotContain("wellbeing", keys);
    }

    [Fact]
    public void Desktop_default_layout_matches_the_catalog_table()
    {
        var work = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, Breakpoints.Desktop);

        Assert.Equal(new GridRect(0, 0, 3, 4), work.Single(i => i.Key == "today-at-a-glance").Rect);
        Assert.Equal(new GridRect(0, 12, 12, 4), work.Single(i => i.Key == "work-pattern").Rect);
        Assert.Equal(new GridRect(8, 4, 4, 6), DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyTeam, Breakpoints.Desktop).Single(i => i.Key == "team-progress").Rect);
    }

    [Fact]
    public void Default_size_is_default_rect_or_min_size()
    {
        Assert.Equal((6, 4), DashboardWidgetRegistry.DefaultSize("my-tasks"));
        Assert.Equal((3, 3), DashboardWidgetRegistry.DefaultSize("notifications"));
        Assert.Equal((3, 3), DashboardWidgetRegistry.DefaultSize("wellbeing"));
        Assert.Equal((8, 6), DashboardWidgetRegistry.DefaultSize("team-status"));
    }

    [Fact]
    public void Tablet_derive_keeps_widgets_at_least_their_min_width()
    {
        var tablet = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyTeam, Breakpoints.Tablet);

        Assert.Equal(4, tablet.Single(i => i.Key == "team-progress").Rect.W);
        Assert.Equal(6, tablet.Single(i => i.Key == "team-status").Rect.W);
    }
}
