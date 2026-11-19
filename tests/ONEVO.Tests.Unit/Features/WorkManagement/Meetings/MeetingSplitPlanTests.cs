using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Spec 5.5 split mapping: old slot i of the series becomes new slot i - materialized occurrences keep their
/// task ids, extra old ones are surplus, and suppressions travel with their slot.</summary>
public sealed class MeetingSplitPlanTests
{
    private static readonly DateTimeOffset P = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    private static List<DateTimeOffset> Weekly(DateTimeOffset from, int count, int hourShift = 0)
        => Enumerable.Range(0, count).Select(i => from.AddDays(7 * i).AddHours(hourShift)).ToList();

    [Fact]
    public void EqualSequences_MapSlotForSlot_InTimeOrder()
    {
        var old = Weekly(P, 4);
        var plan = MeetingSplitPlan.Create(old, materialized: [old[0], old[2]], suppressed: [old[1]], Weekly(P, 4, hourShift: 2));

        Assert.Equal([(old[0], P.AddHours(2)), (old[2], P.AddDays(14).AddHours(2))], plan.Remaps);
        Assert.Equal([(old[1], P.AddDays(7).AddHours(2))], plan.SuppressionCopies);
        Assert.Empty(plan.Surplus);
    }

    [Fact]
    public void FewerNewSlots_MakeTheLaterMaterializedOccurrencesSurplus()
    {
        var old = Weekly(P, 4);
        var plan = MeetingSplitPlan.Create(old, materialized: old, suppressed: [], Weekly(P, 2));

        Assert.Equal(2, plan.Remaps.Count);
        Assert.Equal([old[2], old[3]], plan.Surplus);
    }

    [Fact]
    public void MoreNewSlots_LeaveNothingSurplus_AndKeepNewKeysStrictlyIncreasing()
    {
        var old = Weekly(P, 2);
        var plan = MeetingSplitPlan.Create(old, materialized: old, suppressed: [], Weekly(P, 5, hourShift: -1));

        Assert.Empty(plan.Surplus);
        Assert.Equal(plan.Remaps.Select(r => r.NewKey).Order(), plan.Remaps.Select(r => r.NewKey));
    }

    [Fact]
    public void AMaterializedKeyOutsideTheOldExpansion_IsSurplus()
    {
        var old = Weekly(P, 2);
        var stray = P.AddDays(3);
        var plan = MeetingSplitPlan.Create(old, materialized: [old[0], stray], suppressed: [], Weekly(P, 2));

        Assert.Equal([stray], plan.Surplus);
        Assert.Single(plan.Remaps);
    }
}
