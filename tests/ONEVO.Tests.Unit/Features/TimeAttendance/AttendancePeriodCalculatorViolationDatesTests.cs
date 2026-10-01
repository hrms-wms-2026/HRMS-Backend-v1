using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendancePeriodCalculatorViolationDatesTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t");
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly Guid Employee = Guid.NewGuid();

    private static AttendanceRecord Rec(DateOnly d, string? start, string? end, int? requiredMinutes = null, int workedMinutes = 0) => new()
    {
        Id = Guid.NewGuid(), EmployeeId = Employee, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = start is null ? null : DateTimeOffset.Parse(start),
        ActualEnd = end is null ? null : DateTimeOffset.Parse(end),
        RequiredWorkMinutes = requiredMinutes, WorkedMinutes = workedMinutes
    };

    private static AttendancePeriodData Data(int? allowance, Dictionary<DateOnly, int> breaks, IReadOnlyList<LeaveRequest> leaves, params AttendanceRecord[] records) =>
        new(records, Colombo, DateTimeOffset.Parse("2026-08-21T00:00:00+00:00"), Today, allowance, breaks, leaves,
            DateTimeOffset.Parse("2026-07-31T18:30:00+00:00"), DateTimeOffset.Parse("2026-08-31T18:30:00+00:00"),
            ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(),
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2020, 1, 1), null, Today));

    [Fact]
    public void Classify_ReturnsTheDatesBehindEveryCount()
    {
        var d3 = new DateOnly(2026, 8, 3); var d4 = new DateOnly(2026, 8, 4); var d5 = new DateOnly(2026, 8, 5);
        var d6 = new DateOnly(2026, 8, 6); var d7 = new DateOnly(2026, 8, 7); var sat = new DateOnly(2026, 8, 8);
        var data = Data(null, new(), Array.Empty<LeaveRequest>(),
            Rec(d3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00", 480, 300),
            Rec(d4, "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00"),
            Rec(d5, "2026-08-05T03:30:00+00:00", "2026-08-05T05:30:00+00:00"),
            Rec(d6, "2026-08-06T03:30:00+00:00", null),
            Rec(sat, "2026-08-08T03:30:00+00:00", "2026-08-08T12:00:00+00:00"));

        var c = AttendancePeriodCalculator.Classify(data);

        c.Dates.ShortHours.Should().Equal(d3);
        c.Dates.Late.Should().Equal(d4);
        c.Dates.EarlyDepartures.Should().Equal(d5);
        c.Dates.MissingClockOuts.Should().Equal(d6);
        c.Dates.WorkedOnNonWorkingDay.Should().Equal(sat);
        c.Dates.Absent.Should().Contain(d7);
        c.Dates.Absent.Should().HaveCount(c.Absent);
        c.Dates.Late.Should().HaveCount(c.Late);
        c.Dates.EarlyDepartures.Should().HaveCount(c.EarlyDepartures);
        c.Dates.MissingClockOuts.Should().HaveCount(c.MissingClockOuts);
        c.Dates.ShortHours.Should().HaveCount(c.ShortHours);
        c.Dates.WorkedOnNonWorkingDay.Should().HaveCount(c.WorkedOnNonWorkingDay);
        c.Dates.WorkedDuringTimeOff.Should().HaveCount(c.WorkedDuringTimeOff);
    }

    [Fact]
    public void OverBreakDays_ListsOnlyDaysStrictlyOverTheAllowance()
    {
        var d3 = new DateOnly(2026, 8, 3); var d4 = new DateOnly(2026, 8, 4); var d5 = new DateOnly(2026, 8, 5);
        var data = Data(60, new() { [d3] = 60, [d4] = 75, [d5] = 90 }, Array.Empty<LeaveRequest>(),
            Rec(d3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"),
            Rec(d4, "2026-08-04T03:30:00+00:00", "2026-08-04T12:00:00+00:00"),
            Rec(d5, "2026-08-05T03:30:00+00:00", "2026-08-05T12:00:00+00:00"));

        AttendancePeriodCalculator.OverBreakDays(data).Should().Equal(
            new OverBreakDay(d4, 15), new OverBreakDay(d5, 30));
    }

    [Fact]
    public void OverBreakDays_IsEmpty_WhenNoAllowanceIsConfigured()
    {
        var d4 = new DateOnly(2026, 8, 4);
        var data = Data(null, new() { [d4] = 500 }, Array.Empty<LeaveRequest>(),
            Rec(d4, "2026-08-04T03:30:00+00:00", "2026-08-04T12:00:00+00:00"));

        AttendancePeriodCalculator.OverBreakDays(data).Should().BeEmpty();
    }
}
