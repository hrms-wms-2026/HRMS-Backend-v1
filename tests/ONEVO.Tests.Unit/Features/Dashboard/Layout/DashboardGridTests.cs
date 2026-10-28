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
        var errors = DashboardGrid.Validate([("work-pattern", new GridRect(0, 0, 5, 3))], Breakpoints.Desktop);

        Assert.Contains("SIZE_NOT_ALLOWED", errors);
    }

    [Theory]
    [InlineData("team-status", 12, 8)]
    [InlineData("team-quick-actions", 6, 4)]
    [InlineData("team-progress", 12, 8)]
    [InlineData("team-attendance", 12, 8)]
    public void My_team_widget_min_is_2x2_and_layouts_stored_under_older_minimums_stay_valid(string key, int maxW, int maxH)
    {
        var def = DashboardWidgetRegistry.Get(key)!;
        Assert.Equal((2, 2, maxW, maxH), (def.MinW, def.MinH, def.MaxW, def.MaxH));

        foreach (var breakpoint in new[] { Breakpoints.Desktop, Breakpoints.Tablet })
        {
            Assert.Empty(DashboardGrid.Validate([(key, new GridRect(0, 0, 2, 2))], breakpoint));
            Assert.Empty(DashboardGrid.Validate([(key, new GridRect(0, 0, 3, 3))], breakpoint));
            Assert.Empty(DashboardGrid.Validate([(key, new GridRect(0, 0, 4, 4))], breakpoint));
            Assert.Contains("SIZE_NOT_ALLOWED", DashboardGrid.Validate([(key, new GridRect(0, 0, 1, 2))], breakpoint));
            Assert.Contains("SIZE_NOT_ALLOWED", DashboardGrid.Validate([(key, new GridRect(0, 0, 2, 1))], breakpoint));
        }

        // Every default layout (stored for users who never customised) still validates.
        foreach (var breakpoint in new[] { Breakpoints.Desktop, Breakpoints.Tablet })
        {
            var defaults = DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyTeam, breakpoint)
                .Select(i => (i.Key, i.Rect)).ToList();
            Assert.Empty(DashboardGrid.Validate(defaults, breakpoint));
        }
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

    [Theory]
    [InlineData(int.MaxValue, 0, 2, 3)]
    [InlineData(12, 0, 3, 3)]
    [InlineData(-1, 0, 3, 3)]
    [InlineData(0, -5, 3, 3)]
    [InlineData(0, 0, int.MaxValue, 3)]
    [InlineData(0, 0, 3, int.MaxValue)]
    [InlineData(2, int.MaxValue, 3, 3)]
    [InlineData(0, 198, 3, 3)]
    public void Overflowing_or_out_of_range_rects_are_out_of_bounds(int x, int y, int w, int h)
    {
        var errors = DashboardGrid.Validate([("quick-actions", new GridRect(x, y, w, h))], Breakpoints.Desktop);

        Assert.Contains("OUT_OF_BOUNDS", errors);
    }

    [Fact]
    public void Rect_ending_exactly_at_max_rows_is_valid()
    {
        var errors = DashboardGrid.Validate([("quick-actions", new GridRect(0, DashboardGrid.MaxRows - 3, 3, 3))], Breakpoints.Desktop);

        Assert.Empty(errors);
    }

    [Fact]
    public void Overlaps_is_overflow_safe()
    {
        Assert.False(DashboardGrid.Overlaps(new GridRect(int.MaxValue, 0, 2, 2), new GridRect(0, 0, 3, 3)));
        Assert.True(DashboardGrid.Overlaps(new GridRect(int.MaxValue, 0, 2, 2), new GridRect(int.MaxValue, 1, 1, 1)));
    }

    [Theory]
    [InlineData(0, 1, 12)]
    [InlineData(3, 0, 12)]
    [InlineData(13, 1, 12)]
    [InlineData(3, -1, 12)]
    public void AutoPlace_rejects_invalid_sizes(int w, int h, int columns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DashboardGrid.AutoPlace([], w, h, columns));
    }

    [Fact]
    public void AutoPlace_throws_when_no_slot_within_max_rows()
    {
        var full = new GridRect(0, 0, 12, DashboardGrid.MaxRows);

        Assert.Throws<InvalidOperationException>(() => DashboardGrid.AutoPlace([full], 3, 3, 12));
    }

    [Fact]
    public void Compact_is_correct_for_tall_layouts_and_overflowing_input()
    {
        var result = DashboardGrid.Compact([("a", new GridRect(0, 190, 3, 3)), ("b", new GridRect(int.MaxValue, int.MaxValue, 2, 2))]);

        Assert.Equal(0, result.Single(r => r.Key == "a").Rect.Y);
        Assert.Equal(0, result.Single(r => r.Key == "b").Rect.Y);
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
