using FluentAssertions;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveRequestDayAllocationBuilderTests
{
    private readonly LeaveRequestDayAllocationBuilder _sut = new();

    [Fact]
    public void Build_FullDayDates_ProduceOneUnitEach()
    {
        var rows = _sut.Build(
            [new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 16)],
            null, 3m, 0m);
        rows.Should().HaveCount(3);
        rows.Sum(x => x.DayUnit).Should().Be(3m);
        rows.Sum(x => x.PaidUnit).Should().Be(3m);
    }

    [Fact]
    public void Build_SingleHalfDay_ProducesHalfUnit()
    {
        var rows = _sut.Build([new DateOnly(2026, 9, 14)], "am", 0.5m, 0m);
        rows.Should().ContainSingle();
        rows[0].DayUnit.Should().Be(0.5m);
        rows[0].PaidUnit.Should().Be(0.5m);
    }

    [Fact]
    public void Build_PaidUnitsAllocatedFromRequestSplit()
    {
        var rows = _sut.Build(
            [new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 16)],
            null, 2m, 1m);
        rows.Sum(x => x.PaidUnit).Should().Be(2m);
        rows.Sum(x => x.UnpaidUnit).Should().Be(1m);
        rows[^1].PaidUnit.Should().Be(0m);
        rows[^1].UnpaidUnit.Should().Be(1m);
    }

    [Fact]
    public void Build_UnpaidTailDays_AreNotRestorablePaidDays()
    {
        var rows = _sut.Build(
            [new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15)],
            null, 1m, 1m);
        var futurePaid = rows.Where(x => x.LeaveDate >= new DateOnly(2026, 9, 15)).Sum(x => x.PaidUnit);
        futurePaid.Should().Be(0m);
    }

    [Fact]
    public void Build_MismatchBetweenDatesAndTotals_Throws()
    {
        var act = () => _sut.Build([new DateOnly(2026, 9, 14)], null, 1m, 1m);
        act.Should().Throw<InvalidOperationException>();
    }
}
