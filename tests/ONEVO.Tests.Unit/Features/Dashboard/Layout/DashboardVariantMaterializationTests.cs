using ONEVO.Application.Features.Dashboard.Layout.Commands.AddDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Commands.RemoveDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Domain.Features.Dashboard.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

/// <summary>Add/Remove on an untouched breakpoint keep the viewer's permission-variant (no-gap) layout (Ruling 14 follow-up).</summary>
public sealed class DashboardVariantMaterializationTests
{
    private readonly DashboardMutationFixture _f = new();

    private AddDashboardWidgetCommandHandler Add() => new(_f.Repo.Object, _f.Access.Object, _f.User.Object);
    private RemoveDashboardWidgetCommandHandler Remove() => new(_f.Repo.Object, _f.Access.Object, _f.User.Object);

    private static Dictionary<string, GridRect> Variant(string dashboardKey, IEnumerable<string> allowed, string bp) =>
        DashboardWidgetRegistry.DefaultLayoutFor(dashboardKey, new HashSet<string>(allowed), bp).ToDictionary(i => i.Key, i => i.Rect);

    private static Dictionary<string, GridRect> Stored(UserDashboard d, string bp, Func<DashboardWidget, bool> where) =>
        d.Widgets.Where(where).ToDictionary(w => w.WidgetKey, w => DashboardMutationFixture.Rect(w, bp));

    private static Dictionary<string, GridRect> Compacted(Dictionary<string, GridRect> rects) =>
        DashboardGrid.Compact(rects.Select(kv => (kv.Key, kv.Value)).ToList()).ToDictionary(c => c.Key, c => c.Rect);

    [Fact]
    public async Task Remove_on_untouched_plain_employee_my_work_keeps_no_gap_on_other_breakpoints()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        var target = d.Widgets.Single(w => w.WidgetKey == "my-tasks");
        var hiddenBefore = Stored(d, "desktop", w => w.WidgetKey == "action-center");

        var result = await Remove().Handle(new RemoveDashboardWidgetCommand(d.Id, target.Id, 1), default);

        Assert.True(result.IsSuccess);
        foreach (var bp in Breakpoints.All)
        {
            var expected = Variant(DashboardKeys.MyWork, DashboardMutationFixture.PlainEmployeeWork, bp);
            expected.Remove("my-tasks");
            expected = Compacted(expected);
            var actual = Stored(d, bp, w => w.WidgetKey != "action-center");
            Assert.Equal(expected.OrderBy(k => k.Key), actual.OrderBy(k => k.Key));
        }

        var desktop = Stored(d, "desktop", w => w.WidgetKey != "action-center").Values.ToList();
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 12; x++)
                Assert.Contains(desktop, r => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H);
        Assert.Equal(hiddenBefore, Stored(d, "desktop", w => w.WidgetKey == "action-center"));
    }

    [Fact]
    public async Task Add_on_untouched_plain_employee_my_work_keeps_no_gap()
    {
        string[] allowed = [.. DashboardMutationFixture.PlainEmployeeWork, "wellbeing"];
        var d = _f.Seed(DashboardKeys.MyWork, allowed: allowed);
        var hiddenBefore = Stored(d, "desktop", w => w.WidgetKey == "action-center");

        var result = await Add().Handle(new AddDashboardWidgetCommand(d.Id, "wellbeing", 1, null), default);

        Assert.True(result.IsSuccess);
        foreach (var bp in Breakpoints.All)
        {
            // The variant is already compact, so existing widgets keep their variant rects.
            var variant = Variant(DashboardKeys.MyWork, DashboardMutationFixture.PlainEmployeeWork, bp);
            var others = Stored(d, bp, w => w.WidgetKey is not ("wellbeing" or "action-center"));
            Assert.Equal(variant.OrderBy(k => k.Key), others.OrderBy(k => k.Key));
            var items = d.Widgets.Where(w => w.WidgetKey != "action-center")
                .Select(w => (w.WidgetKey, DashboardMutationFixture.Rect(w, bp))).ToList();
            Assert.Empty(DashboardGrid.Validate(items, bp));
        }

        Assert.Equal(hiddenBefore, Stored(d, "desktop", w => w.WidgetKey == "action-center"));
    }

    [Fact]
    public async Task Add_on_untouched_lead_without_team_status_keeps_team_progress_wide()
    {
        string[] allowed = ["team-progress", "team-quick-actions"];
        var d = _f.Seed(DashboardKeys.MyTeam, allowed: allowed);
        // The lead removed quick actions earlier, leaving team-progress at its full-default rect.
        var quick = d.Widgets.Single(w => w.WidgetKey == "team-quick-actions");
        quick.Layouts.Clear();
        d.Widgets.Remove(quick);

        var result = await Add().Handle(new AddDashboardWidgetCommand(d.Id, "team-quick-actions", 1, null), default);

        Assert.True(result.IsSuccess);
        foreach (var bp in Breakpoints.All)
        {
            var variant = Variant(DashboardKeys.MyTeam, allowed, bp);
            Assert.Equal(variant["team-progress"], Stored(d, bp, w => w.WidgetKey == "team-progress")["team-progress"]);
            var items = d.Widgets.Where(w => w.WidgetKey != "team-status")
                .Select(w => (w.WidgetKey, DashboardMutationFixture.Rect(w, bp))).ToList();
            Assert.Empty(DashboardGrid.Validate(items, bp));
        }

        Assert.Equal(new GridRect(0, 0, 8, 6), Stored(d, "desktop", w => w.WidgetKey == "team-progress")["team-progress"]);
    }

    [Fact]
    public async Task Customized_breakpoint_is_not_rewritten_by_add_or_remove()
    {
        var d = _f.Seed(DashboardKeys.MyWork);
        // The user moved a widget on tablet, so tablet is no longer the full default.
        d.Widgets.Single(w => w.WidgetKey == "quick-actions").Layouts.Single(l => l.Breakpoint == "tablet").Y += 20;
        var tabletBefore = Stored(d, "tablet", w => w.WidgetKey != "action-center");
        var target = d.Widgets.Single(w => w.WidgetKey == "work-pattern");

        var result = await Remove().Handle(new RemoveDashboardWidgetCommand(d.Id, target.Id, 1), default);

        Assert.True(result.IsSuccess);
        var tabletAfter = Stored(d, "tablet", w => w.WidgetKey != "action-center");
        var variantTablet = Variant(DashboardKeys.MyWork, DashboardMutationFixture.PlainEmployeeWork, "tablet");
        Assert.NotEqual(variantTablet["quick-actions"], tabletAfter["quick-actions"]);
        tabletBefore.Remove("work-pattern");
        Assert.Equal(Compacted(tabletBefore).OrderBy(k => k.Key), tabletAfter.OrderBy(k => k.Key));

        // Desktop was untouched: it is now the variant (minus the removed widget).
        var variantDesktop = Variant(DashboardKeys.MyWork, DashboardMutationFixture.PlainEmployeeWork, "desktop");
        variantDesktop.Remove("work-pattern");
        Assert.Equal(Compacted(variantDesktop).OrderBy(k => k.Key),
            Stored(d, "desktop", w => w.WidgetKey != "action-center").OrderBy(k => k.Key));
    }

    [Fact]
    public async Task Viewer_with_all_gated_widgets_is_unchanged()
    {
        var all = DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyWork).Select(x => x.Key).ToArray();
        var d = _f.Seed(DashboardKeys.MyWork, allowed: all);
        var target = d.Widgets.Single(w => w.WidgetKey == "my-tasks");
        var full = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, "desktop").ToDictionary(i => i.Key, i => i.Rect);
        full.Remove("my-tasks");

        var result = await Remove().Handle(new RemoveDashboardWidgetCommand(d.Id, target.Id, 1), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Compacted(full).OrderBy(k => k.Key), Stored(d, "desktop", _ => true).OrderBy(k => k.Key));
        Assert.Equal(new GridRect(0, 3, 5, 3), Stored(d, "desktop", w => w.WidgetKey == "action-center")["action-center"]);
    }
}
