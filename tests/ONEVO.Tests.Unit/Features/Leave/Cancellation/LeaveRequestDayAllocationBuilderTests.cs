using FluentAssertions;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveRequestDayAllocationBuilderTests
{
    private readonly LeaveRequestDayAllocationBuilder _sut = new();

    [Fact]
    public void Build_FullShifts_UseWorkDayHoursEach()
    {
        var rows = _sut.Build(
            [new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 16)],
            [8m, 8m, 8m],
            24m, 0m);
        rows.Should().HaveCount(3);
        rows.Sum(x => x.HoursUnit).Should().Be(24m);
        rows.Sum(x => x.PaidHoursUnit).Should().Be(24m);
    }

    [Fact]
    public void Build_PartialAfternoon_UsesOverlapHours()
    {
        var rows = _sut.Build([new DateOnly(2026, 9, 14)], [4m], 4m, 0m);
        rows.Should().ContainSingle();
        rows[0].HoursUnit.Should().Be(4m);
        rows[0].PaidHoursUnit.Should().Be(4m);
    }

    [Fact]
    public void Build_PaidHoursAllocatedFromRequestSplit()
    {
        var rows = _sut.Build(
            [new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 16)],
            [8m, 8m, 8m],
            16m, 8m);
        rows.Sum(x => x.PaidHoursUnit).Should().Be(16m);
        rows.Sum(x => x.UnpaidHoursUnit).Should().Be(8m);
        rows[^1].PaidHoursUnit.Should().Be(0m);
        rows[^1].UnpaidHoursUnit.Should().Be(8m);
    }

    [Fact]
    public void Build_UnpaidTailHours_AreNotRestorablePaidHours()
    {
        var rows = _sut.Build(
            [new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15)],
            [8m, 8m],
            8m, 8m);
        var futurePaid = rows.Where(x => x.LeaveDate >= new DateOnly(2026, 9, 15)).Sum(x => x.PaidHoursUnit);
        futurePaid.Should().Be(0m);
    }

    [Fact]
    public void Build_MismatchBetweenHoursAndTotals_Throws()
    {
        var act = () => _sut.Build([new DateOnly(2026, 9, 14)], [8m], 8m, 8m);
        act.Should().Throw<InvalidOperationException>();
    }
}
