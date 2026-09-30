using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendancePeriodCalculatorTests
{
    private static readonly TimeZoneInfo Colombo =
        TimeZoneInfo.CreateCustomTimeZone("test-colombo", TimeSpan.FromHours(5.5), "test", "test");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T00:00:00+00:00");
    private static readonly DateOnly Today = new(2026, 8, 21);

    private static AttendanceRecord Record(DateOnly date, string? actualStartUtc = null, string? actualEndUtc = null, bool working = true) =>
        new()
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Date = date,
            ExpectedWorkingDay = working,
            ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
            ActualStart = actualStartUtc is null ? null : DateTimeOffset.Parse(actualStartUtc),
            ActualEnd = actualEndUtc is null ? null : DateTimeOffset.Parse(actualEndUtc)
        };

    private static readonly AttendanceRecord OnTime = Record(new(2026, 8, 3), "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00");
    private static readonly AttendanceRecord Late = Record(new(2026, 8, 4), "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00");
    private static readonly AttendanceRecord EarlyOut = Record(new(2026, 8, 5), "2026-08-05T03:30:00+00:00", "2026-08-05T05:30:00+00:00");
    private static readonly AttendanceRecord MissingOut = Record(new(2026, 8, 6), "2026-08-06T03:30:00+00:00");
    private static readonly AttendanceRecord NeverIn = Record(new(2026, 8, 7));

    [Fact]
    public void Count_MatchesTheMonthlySummaryRules()
    {
        var counts = AttendancePeriodCalculator.Count(new[] { OnTime, Late, EarlyOut, MissingOut, NeverIn }, Colombo, Now);

        counts.WorkingDays.Should().Be(5);
        counts.DaysPresent.Should().Be(4);
        counts.LateArrivals.Should().Be(1);
        counts.EarlyDepartures.Should().Be(1);
        counts.MissingClockOuts.Should().Be(1);
    }

    [Fact]
    public void Count_OfNothing_IsAllZero()
    {
        var counts = AttendancePeriodCalculator.Count(Array.Empty<AttendanceRecord>(), Colombo, Now);

        counts.Should().Be(new AttendancePeriodCounts(0, 0, 0, 0, 0));
    }

    [Fact]
    public void DayStatus_ClassifiesEveryDay()
    {
        AttendancePeriodCalculator.DayStatus(OnTime, Colombo, Now, Today, false).Should().Be("present");
        AttendancePeriodCalculator.DayStatus(Late, Colombo, Now, Today, false).Should().Be("late");
        AttendancePeriodCalculator.DayStatus(EarlyOut, Colombo, Now, Today, false).Should().Be("present");
        AttendancePeriodCalculator.DayStatus(MissingOut, Colombo, Now, Today, false).Should().Be("missing_clock_out");
        AttendancePeriodCalculator.DayStatus(NeverIn, Colombo, Now, Today, false).Should().Be("absent");
    }

    [Fact]
    public void DayStatus_LeaveBeatsAbsent_OffDayIsOff_AndTodayNotYetInIsNone()
    {
        AttendancePeriodCalculator.DayStatus(NeverIn, Colombo, Now, Today, hasApprovedLeave: true).Should().Be("leave");
        AttendancePeriodCalculator.DayStatus(Record(new(2026, 8, 8), working: false), Colombo, Now, Today, false).Should().Be("off");
        AttendancePeriodCalculator.DayStatus(Record(Today), Colombo, Now, Today, false).Should().Be("none");
    }

    [Fact]
    public void DayStatus_WorkedThroughApprovedLeave_StillCountsAsPresent()
    {
        AttendancePeriodCalculator.DayStatus(OnTime, Colombo, Now, Today, hasApprovedLeave: true).Should().Be("present");
    }

    [Fact]
    public void CoversDate_IsInclusiveOnBothEnds_UsingUtcDates()
    {
        var leave = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            StartAt = DateTimeOffset.Parse("2026-08-10T00:00:00+00:00"),
            EndAt = DateTimeOffset.Parse("2026-08-12T23:59:59+00:00")
        };

        AttendancePeriodCalculator.CoversDate(leave, new DateOnly(2026, 8, 9)).Should().BeFalse();
        AttendancePeriodCalculator.CoversDate(leave, new DateOnly(2026, 8, 10)).Should().BeTrue();
        AttendancePeriodCalculator.CoversDate(leave, new DateOnly(2026, 8, 12)).Should().BeTrue();
        AttendancePeriodCalculator.CoversDate(leave, new DateOnly(2026, 8, 13)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/A_Zone")]
    public void ResolveTimezone_FallsBackToUtc(string? id)
    {
        AttendancePeriodCalculator.ResolveTimezone(id).Should().Be(TimeZoneInfo.Utc);
    }
}
