using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class DashboardGridTests
{
    [Fact]
    public void Overlap_detected_for_shared_cell()
    {
        Assert.True(DashboardGrid.Overlaps(new GridRect(0, 0, 3, 3), new GridRect(2, 2, 3, 3)));
    }

    [Fact]
    public void Touching_edges_do_not_overlap()
    {
        Assert.False(DashboardGrid.Overlaps(new GridRect(0, 0, 3, 3), new GridRect(3, 0, 3, 3)));
        Assert.False(DashboardGrid.Overlaps(new GridRect(0, 0, 3, 3), new GridRect(0, 3, 3, 3)));
    }

    [Fact]
    public void Out_of_bounds_when_x_plus_w_exceeds_columns()
    {
        var errors = DashboardGrid.Validate([("quick-actions", new GridRect(10, 0, 3, 3))], Breakpoints.Desktop);

        Assert.Contains("OUT_OF_BOUNDS", errors);
    }

    [Fact]
    public void Size_below_min_rejected()
    {
        var errors = DashboardGrid.Validate([("team-status", new GridRect(0, 0, 5, 4))], Breakpoints.Desktop);

        Assert.Contains("SIZE_NOT_ALLOWED", errors);
    }

    [Fact]
    public void Unknown_and_duplicate_widgets_rejected()
    {
        var errors = DashboardGrid.Validate(
        [
            ("nope", new GridRect(0, 0, 3, 3)),
            ("quick-actions", new GridRect(3, 0, 3, 3)),
            ("quick-actions", new GridRect(6, 0, 3, 3)),
        ], Breakpoints.Desktop);

        Assert.Contains("UNKNOWN_WIDGET", errors);
        Assert.Contains("DUPLICATE_WIDGET", errors);
    }

    [Fact]
    public void Overlapping_items_rejected_and_valid_layout_passes()
    {
        var overlapping = DashboardGrid.Validate(
            [("quick-actions", new GridRect(0, 0, 3, 3)), ("notifications", new GridRect(2, 1, 3, 3))],
            Breakpoints.Desktop);
        var ok = DashboardGrid.Validate(
            [("quick-actions", new GridRect(0, 0, 3, 3)), ("notifications", new GridRect(3, 0, 3, 3))],
            Breakpoints.Desktop);

        Assert.Contains("OVERLAP", overlapping);
        Assert.Empty(ok);
    }

    [Fact]
    public void AutoPlace_picks_first_free_top_left_slot()
    {
        var placed = DashboardGrid.AutoPlace([new GridRect(0, 0, 6, 4), new GridRect(6, 0, 6, 2)], 6, 2, 12);

        Assert.Equal(new GridRect(6, 2, 6, 2), placed);
    }

    [Fact]
    public void Compact_floats_widget_into_hole()
    {
        var result = DashboardGrid.Compact([("a", new GridRect(0, 0, 3, 4)), ("b", new GridRect(6, 4, 3, 4))]);

        Assert.Equal(new GridRect(6, 0, 3, 4), result.Single(r => r.Key == "b").Rect);
        Assert.Equal(new GridRect(0, 0, 3, 4), result.Single(r => r.Key == "a").Rect);
    }

    [Fact]
    public void Compact_never_creates_overlap()
    {
        var result = DashboardGrid.Compact(
        [
            ("a", new GridRect(0, 0, 6, 3)),
            ("b", new GridRect(3, 5, 6, 3)),
            ("c", new GridRect(0, 9, 4, 2)),
            ("d", new GridRect(8, 4, 4, 4)),
        ]);

        for (var i = 0; i < result.Count; i++)
        {
            for (var j = i + 1; j < result.Count; j++)
            {
                Assert.False(DashboardGrid.Overlaps(result[i].Rect, result[j].Rect));
            }
        }

        Assert.Equal(new GridRect(3, 4, 6, 3), result.Single(r => r.Key == "b").Rect);
    }
}
