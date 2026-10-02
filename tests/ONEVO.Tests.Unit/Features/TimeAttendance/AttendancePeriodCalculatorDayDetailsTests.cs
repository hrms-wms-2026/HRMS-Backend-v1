using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendancePeriodCalculatorDayDetailsTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t");
    private static readonly DateOnly Today = new(2026, 8, 7);
    private static readonly Guid Employee = Guid.NewGuid();

    private static AttendanceRecord Rec(DateOnly d, string? start, string? end, int? required = null, int worked = 0) => new()
    {
        Id = Guid.NewGuid(), EmployeeId = Employee, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = start is null ? null : DateTimeOffset.Parse(start),
        ActualEnd = end is null ? null : DateTimeOffset.Parse(end),
        RequiredWorkMinutes = required, WorkedMinutes = worked, ExpectedWorkModeName = "Onsite"
    };

    private static AttendancePeriodData Data(int? allowance, Dictionary<DateOnly, int> breaks, params AttendanceRecord[] records) =>
        new(records, Colombo, DateTimeOffset.Parse("2026-08-07T00:00:00+00:00"), Today, allowance, breaks, Array.Empty<LeaveRequest>(),
            DateTimeOffset.Parse("2026-07-31T18:30:00+00:00"), DateTimeOffset.Parse("2026-08-31T18:30:00+00:00"),
            ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(),
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2020, 1, 1), null, Today));

    [Fact]
    public void DayDetails_ListsPastDaysNewestFirst_WithTheCardStatuses()
    {
        var d3 = new DateOnly(2026, 8, 3);
        var d4 = new DateOnly(2026, 8, 4);
        var data = Data(null, new(),
            Rec(d3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"),
            Rec(d4, "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00"));

        var days = AttendancePeriodCalculator.DayDetails(data);

        days.Select(d => d.Date).Should().BeInDescendingOrder();
        days.Should().NotContain(d => d.Date > Today);
        days.Single(d => d.Date == d3).Status.Should().Be("present");
        days.Single(d => d.Date == d4).Status.Should().Be("late");
        days.Single(d => d.Date == new DateOnly(2026, 8, 5)).Status.Should().Be("absent");
        days.Single(d => d.Date == new DateOnly(2026, 8, 1)).Status.Should().Be("off");
        // Same statuses as the card strip.
        var strip = AttendancePeriodCalculator.Classify(data).Days.ToDictionary(d => d.Date, d => d.Status);
        days.Should().OnlyContain(d => strip[d.Date] == d.Status);
    }

    [Fact]
    public void DayDetails_CarriesLocalClockTimes_LateEarlyAndShortHours()
    {
        var d4 = new DateOnly(2026, 8, 4);
        var data = Data(null, new(),
            // 09:45 -> 17:00 local: 45 min late, 30 min early, worked 300 of 480.
            Rec(d4, "2026-08-04T04:15:00+00:00", "2026-08-04T11:30:00+00:00", required: 480, worked: 300));

        var day = AttendancePeriodCalculator.DayDetails(data).Single(d => d.Date == d4);

        day.ClockInLocal.Should().Be("09:45");
        day.ClockOutLocal.Should().Be("17:00");
        day.ScheduledStart.Should().Be("09:00");
        day.LateMinutes.Should().Be(45);
        day.EarlyLeaveMinutes.Should().Be(30);
        day.WorkedMinutes.Should().Be(300);
        day.ShortHours.Should().BeTrue();
        day.WorkMode.Should().Be("Onsite");
    }

    [Fact]
    public void DayDetails_UsesTheBreakTotals_AndMinutesOverTheAllowance()
    {
        var d3 = new DateOnly(2026, 8, 3);
        var data = Data(60, new() { [d3] = 85 },
            Rec(d3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"));

        var day = AttendancePeriodCalculator.DayDetails(data).Single(d => d.Date == d3);

        day.BreakMinutes.Should().Be(85);
        day.OverBreakMinutes.Should().Be(25);
        day.LateMinutes.Should().Be(0);
    }
}
