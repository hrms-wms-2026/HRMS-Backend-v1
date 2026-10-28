using ONEVO.Application.Features.WorkManagement.Planner.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Today's Team Schedule (2026-10-29 design, section 6.3): the 60/40 execution score and its ranking.</summary>
public class ExecutionScoreTests
{
    [Fact]
    public void Score_is_weighted_60_40()
    {
        var r = ExecutionScore.Compute(dueItems: 4, completedDueItems: 3, elapsedPlannedTaskMinutes: 120, scheduledAttributedMinutes: 90,
            onLeave: false, plannerEnabled: true);

        Assert.Equal((75.0m, 75.0m, 75.0m, NotRankedReason.None), (r.ItemCompletionPercent, r.TimeCoveragePercent, r.Score, r.Reason));
    }

    [Fact]
    public void Score_mixes_different_parts()
    {
        // 0.6 x 50 + 0.4 x 100 = 70
        var r = ExecutionScore.Compute(2, 1, 60, 60, false, true);

        Assert.Equal(70.0m, r.Score);
    }

    [Fact]
    public void Coverage_capped_at_100()
    {
        var r = ExecutionScore.Compute(1, 1, 60, 90, false, true);

        Assert.Equal(100m, r.TimeCoveragePercent);
        Assert.Equal(100m, r.Score);
    }

    [Fact]
    public void No_due_items_is_not_ranked_NoItemsYet()
    {
        var r = ExecutionScore.Compute(0, 0, 60, 30, false, true);

        Assert.Equal((null, 50.0m, null, NotRankedReason.NoItemsYet), (r.ItemCompletionPercent, r.TimeCoveragePercent, r.Score, r.Reason));
    }

    [Fact]
    public void Only_items_no_planned_time_gives_item_part_but_no_score()
    {
        var r = ExecutionScore.Compute(2, 1, 0, 0, false, true);

        Assert.Equal((50.0m, null, null, NotRankedReason.NoPlannedTaskTime), (r.ItemCompletionPercent, r.TimeCoveragePercent, r.Score, r.Reason));
    }

    [Fact]
    public void Leave_is_not_ranked()
    {
        var r = ExecutionScore.Compute(2, 2, 60, 60, onLeave: true, plannerEnabled: true);

        Assert.Equal((null, NotRankedReason.OnLeave), (r.Score, r.Reason));
    }

    [Fact]
    public void Planner_off_is_not_ranked()
    {
        var r = ExecutionScore.Compute(2, 2, 60, 60, onLeave: false, plannerEnabled: false);

        Assert.Equal((null, null, null, NotRankedReason.PlannerOff), (r.ItemCompletionPercent, r.TimeCoveragePercent, r.Score, r.Reason));
    }

    private sealed record Row(string Name, ExecutionScoreResult Result);

    [Fact]
    public void Ties_break_on_item_then_time_then_name()
    {
        // All score 70: (50,100) vs (100,25) differ on item; the two (50,100) rows tie fully and sort by name.
        var rows = new[]
        {
            new Row("Zoe", ExecutionScore.Compute(2, 1, 60, 60, false, true)),
            new Row("Unranked", ExecutionScore.Compute(0, 0, 0, 0, false, true)),
            new Row("Amy", ExecutionScore.Compute(2, 1, 60, 60, false, true)),
            new Row("Bob", ExecutionScore.Compute(4, 4, 100, 25, false, true)),
            new Row("Top", ExecutionScore.Compute(1, 1, 60, 60, false, true)),
            new Row("Absent", ExecutionScore.Compute(1, 1, 60, 60, true, true)),
        };

        var ranked = ExecutionScore.Rank(rows, r => r.Result, r => r.Name);

        Assert.Equal(new[] { "Top", "Bob", "Amy", "Zoe", "Absent", "Unranked" }, ranked.Select(r => r.Name));
    }
}
