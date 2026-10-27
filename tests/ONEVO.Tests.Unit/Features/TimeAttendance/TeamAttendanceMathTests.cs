using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Team.Services;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class TeamAttendanceMathTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    [Theory]
    [InlineData("week", TeamAttendanceMode.Week)]
    [InlineData("Month", TeamAttendanceMode.Month)]
    public void TryParseMode_AcceptsWeekAndMonth(string raw, TeamAttendanceMode expected)
    {
        TeamAttendanceMath.TryParseMode(raw, out var mode).Should().BeTrue();
        mode.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("year")]
    public void TryParseMode_RejectsOthers(string? raw) =>
        TeamAttendanceMath.TryParseMode(raw, out _).Should().BeFalse();

    [Fact]
    public void Resolve_Week_IsIsoMondayToSunday()
    {
        var r = TeamAttendanceMath.Resolve(TeamAttendanceMode.Week, D("2026-10-28"), D("2026-11-05"));
        r.From.Should().Be(D("2026-10-26"));
        r.To.Should().Be(D("2026-11-01"));
        r.PrevFrom.Should().Be(D("2026-10-19"));
        r.PrevTo.Should().Be(D("2026-10-25"));
        r.InProgress.Should().BeFalse();
    }

    [Fact]
    public void Resolve_InProgressWeek_ClipsPrevious()
    {
        var r = TeamAttendanceMath.Resolve(TeamAttendanceMode.Week, D("2026-10-28"), D("2026-10-28"));
        r.PrevFrom.Should().Be(D("2026-10-19"));
        r.PrevTo.Should().Be(D("2026-10-21"));
        r.InProgress.Should().BeTrue();
    }

    [Fact]
    public void Resolve_SundayAnchor_BelongsToThatWeek()
    {
        var r = TeamAttendanceMath.Resolve(TeamAttendanceMode.Week, D("2026-11-01"), D("2026-12-01"));
        r.From.Should().Be(D("2026-10-26"));
        r.To.Should().Be(D("2026-11-01"));
    }

    [Fact]
    public void Resolve_LeapFebruary_Has29Days()
    {
        var r = TeamAttendanceMath.Resolve(TeamAttendanceMode.Month, D("2028-02-10"), D("2028-06-01"));
        r.From.Should().Be(D("2028-02-01"));
        r.To.Should().Be(D("2028-02-29"));
        r.PrevFrom.Should().Be(D("2028-01-01"));
        r.PrevTo.Should().Be(D("2028-01-31"));
    }

    [Fact]
    public void Resolve_MonthClipsPreviousToItsLength()
    {
        var r = TeamAttendanceMath.Resolve(TeamAttendanceMode.Month, D("2028-03-31"), D("2028-03-31"));
        r.From.Should().Be(D("2028-03-01"));
        r.To.Should().Be(D("2028-03-31"));
        r.PrevFrom.Should().Be(D("2028-02-01"));
        r.PrevTo.Should().Be(D("2028-02-29"));
        r.InProgress.Should().BeTrue();
    }

    [Fact]
    public void Resolve_InProgressMonth_ClipsPreviousToElapsedDays()
    {
        var r = TeamAttendanceMath.Resolve(TeamAttendanceMode.Month, D("2026-10-27"), D("2026-10-27"));
        r.PrevFrom.Should().Be(D("2026-09-01"));
        r.PrevTo.Should().Be(D("2026-09-27"));
    }

    [Fact]
    public void Resolve_JanuaryPreviousIsDecemberPriorYear()
    {
        var r = TeamAttendanceMath.Resolve(TeamAttendanceMode.Month, D("2027-01-15"), D("2027-03-01"));
        r.PrevFrom.Should().Be(D("2026-12-01"));
        r.PrevTo.Should().Be(D("2026-12-31"));
    }

    [Theory]
    [InlineData("present", false, true, TeamAttendanceBuckets.Present)]
    [InlineData("present", true, true, TeamAttendanceBuckets.Late)]
    [InlineData("missing_clock_out", false, true, TeamAttendanceBuckets.Present)]
    [InlineData("missing_clock_out", true, true, TeamAttendanceBuckets.Late)]
    [InlineData("late", false, true, TeamAttendanceBuckets.Late)]
    [InlineData("absent", false, true, TeamAttendanceBuckets.Absent)]
    [InlineData("leave", false, true, TeamAttendanceBuckets.Leave)]
    [InlineData("leave", false, false, TeamAttendanceBuckets.Absent)]
    [InlineData("off", false, true, TeamAttendanceBuckets.Off)]
    [InlineData("none", false, true, TeamAttendanceBuckets.Off)]
    public void Bucket_MapsClassifyStatus(string status, bool isLate, bool leaveAuthorized, string expected) =>
        TeamAttendanceMath.Bucket(status, isLate, leaveAuthorized).Should().Be(expected);

    [Fact]
    public void Rate_ZeroDenominator_IsNull() => TeamAttendanceMath.Rate(0, 0).Should().BeNull();

    [Fact]
    public void Rate_24Of24_Is100() => TeamAttendanceMath.Rate(24, 24).Should().Be(100m);

    [Fact]
    public void Rate_RoundsToOneDecimal() => TeamAttendanceMath.Rate(2, 3).Should().Be(66.7m);
}
