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
        Assert.Equal(4, team.Count);
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
            ["today-at-a-glance", "todays-schedule", "quick-actions", "action-center", "today-total-time", "active-projects", "my-tasks", "work-pattern"],
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

        Assert.Equal(new GridRect(0, 0, 4, 3), work.Single(i => i.Key == "today-at-a-glance").Rect);
        Assert.Equal(new GridRect(0, 3, 5, 3), work.Single(i => i.Key == "action-center").Rect);
        Assert.Equal(new GridRect(0, 10, 12, 4), work.Single(i => i.Key == "work-pattern").Rect);
        Assert.Equal(new GridRect(8, 4, 4, 6), DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyTeam, Breakpoints.Desktop).Single(i => i.Key == "team-progress").Rect);
        Assert.Equal(new GridRect(0, 6, 8, 6), DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyTeam, Breakpoints.Desktop).Single(i => i.Key == "team-attendance").Rect);
    }

    [Fact]
    public void Desktop_default_layout_is_a_copy_not_the_shared_array()
    {
        var first = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, Breakpoints.Desktop);

        Assert.False(first is (string, GridRect)[]);
        Assert.NotSame(first, DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, Breakpoints.Desktop));
    }

    [Fact]
    public void Default_size_is_default_rect_or_min_size()
    {
        Assert.Equal((6, 4), DashboardWidgetRegistry.DefaultSize("my-tasks"));
        Assert.Equal((3, 3), DashboardWidgetRegistry.DefaultSize("notifications"));
        Assert.Equal((3, 3), DashboardWidgetRegistry.DefaultSize("wellbeing"));
        Assert.Equal((8, 6), DashboardWidgetRegistry.DefaultSize("team-status"));
        Assert.Equal((8, 6), DashboardWidgetRegistry.DefaultSize("team-attendance"));
    }

    [Fact]
    public void Tablet_derive_keeps_widgets_at_least_their_min_width()
    {
        var tablet = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyTeam, Breakpoints.Tablet);

        Assert.Equal(4, tablet.Single(i => i.Key == "team-progress").Rect.W);
        Assert.Equal(6, tablet.Single(i => i.Key == "team-status").Rect.W);
    }
    private static readonly string[] AllWork =
        DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyWork).Select(d => d.Key).ToArray();

    public static TheoryData<string, string[]> VariantCases() => new()
    {
        { DashboardKeys.MyWork, AllWork.Where(k => k != "action-center").ToArray() },
        { DashboardKeys.MyTeam, ["team-quick-actions", "team-progress"] },
        { DashboardKeys.MyTeam, ["team-quick-actions"] },
    };

    [Theory]
    [MemberData(nameof(VariantCases))]
    public void Every_variant_is_valid_on_all_breakpoints_and_respects_min_max(string dashboard, string[] allowed)
    {
        var set = new HashSet<string>(allowed);
        Assert.True(DashboardWidgetRegistry.HasVariant(dashboard, set));
        foreach (var breakpoint in Breakpoints.All)
        {
            var layout = DashboardWidgetRegistry.DefaultLayoutFor(dashboard, set, breakpoint);

            Assert.NotEmpty(layout);
            Assert.Empty(DashboardGrid.Validate(layout, breakpoint));
            Assert.All(layout, i => Assert.Contains(i.Key, allowed));
            Assert.Equal(
                DashboardWidgetRegistry.Derive(DashboardWidgetRegistry.DefaultLayoutFor(dashboard, set, Breakpoints.Desktop), breakpoint),
                layout);
        }

        foreach (var (key, rect) in DashboardWidgetRegistry.DefaultLayoutFor(dashboard, set, Breakpoints.Desktop))
        {
            var def = DashboardWidgetRegistry.Get(key)!;
            Assert.InRange(rect.W, def.MinW, def.MaxW);
            Assert.InRange(rect.H, def.MinH, def.MaxH);
        }
    }

    [Fact]
    public void My_work_without_action_center_variant_matches_ruling()
    {
        var layout = DashboardWidgetRegistry.DefaultLayoutFor(
            DashboardKeys.MyWork, new HashSet<string>(AllWork.Where(k => k != "action-center")), Breakpoints.Desktop);

        Assert.Equal(
            [
                ("today-at-a-glance", new GridRect(0, 0, 4, 3)),
                ("todays-schedule", new GridRect(4, 0, 5, 3)),
                ("quick-actions", new GridRect(9, 0, 3, 3)),
                ("today-total-time", new GridRect(0, 3, 6, 3)),
                ("active-projects", new GridRect(6, 3, 6, 3)),
                ("my-tasks", new GridRect(0, 6, 6, 4)),
                ("work-pattern", new GridRect(0, 10, 12, 4)),
            ],
            layout);
    }

    [Fact]
    public void My_team_variants_match_ruling()
    {
        Assert.Equal(
            [("team-progress", new GridRect(0, 0, 8, 6)), ("team-quick-actions", new GridRect(8, 0, 4, 4))],
            DashboardWidgetRegistry.DefaultLayoutFor(
                DashboardKeys.MyTeam, new HashSet<string> { "team-quick-actions", "team-progress" }, Breakpoints.Desktop));
        Assert.Equal(
            [("team-quick-actions", new GridRect(8, 0, 4, 4))],
            DashboardWidgetRegistry.DefaultLayoutFor(
                DashboardKeys.MyTeam, new HashSet<string> { "team-quick-actions" }, Breakpoints.Desktop));
    }

    [Fact]
    public void Full_permissions_select_no_variant_and_return_full_default()
    {
        var team = new HashSet<string> { "team-quick-actions", "team-status", "team-progress" };
        var statusOnly = new HashSet<string> { "team-quick-actions", "team-status" };

        Assert.False(DashboardWidgetRegistry.HasVariant(DashboardKeys.MyWork, new HashSet<string>(AllWork)));
        Assert.False(DashboardWidgetRegistry.HasVariant(DashboardKeys.MyTeam, team));
        Assert.False(DashboardWidgetRegistry.HasVariant(DashboardKeys.MyTeam, statusOnly));
        foreach (var breakpoint in Breakpoints.All)
        {
            Assert.Equal(
                DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, breakpoint),
                DashboardWidgetRegistry.DefaultLayoutFor(DashboardKeys.MyWork, new HashSet<string>(AllWork), breakpoint));
            Assert.Equal(
                DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyTeam, breakpoint),
                DashboardWidgetRegistry.DefaultLayoutFor(DashboardKeys.MyTeam, statusOnly, breakpoint));
        }
    }

    [Fact]
    public void Tablet_width_rule_is_shared()
    {
        var progress = DashboardWidgetRegistry.Get("team-progress")!;
        var tasks = DashboardWidgetRegistry.Get("my-tasks")!;

        Assert.Equal(4, DashboardWidgetRegistry.TabletWidth(progress, 4));
        Assert.Equal(6, DashboardWidgetRegistry.TabletWidth(progress, 8));
        Assert.Equal(3, DashboardWidgetRegistry.TabletWidth(tasks, 3));
        Assert.Equal(6, DashboardWidgetRegistry.TabletWidth(tasks, 12));
        Assert.Equal(3, DashboardWidgetRegistry.TabletWidth(null, 2));
    }

    [Fact]
    public void Dashboard_titles_come_from_DashboardKeys()
    {
        Assert.Equal("My Work", DashboardKeys.Title(DashboardKeys.MyWork));
        Assert.Equal("My Team", DashboardKeys.Title(DashboardKeys.MyTeam));
    }
}
