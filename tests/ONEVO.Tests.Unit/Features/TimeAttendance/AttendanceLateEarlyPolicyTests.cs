using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

/// <summary>Late/early under the clock-in policy: the first row is the threshold, approved time off moves the
/// schedule edges, and short hours is retired.</summary>
public sealed class AttendanceLateEarlyPolicyTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly DateOnly From = new(2026, 9, 1), To = new(2026, 9, 30), Today = new(2026, 9, 30);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T12:00:00+00:00");

    private static AttendancePeriodData Data(AttendanceRecord record, int threshold = 0, params LeaveRequest[] leaves) =>
        new([record], TimeZoneInfo.Utc, Now, Today, 60, new Dictionary<DateOnly, int>(), leaves,
            DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
            ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(), From, To, new DateOnly(2020, 1, 1), null, Today),
            LateEarlyThresholdMinutes: threshold);

    private static DateTimeOffset At(int hour, int minute) => new(Day.Year, Day.Month, Day.Day, hour, minute, 0, TimeSpan.Zero);

    private static AttendanceRecord Record(DateTimeOffset start, DateTimeOffset? end, int required = 480, int worked = 480) => new()
    {
        Id = Guid.NewGuid(), Date = Day, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 0),
        ActualStart = start, ActualEnd = end, RequiredWorkMinutes = required, WorkedMinutes = worked,
    };

    private static LeaveRequest Leave(DateTimeOffset start, DateTimeOffset end) => new() { Id = Guid.NewGuid(), StartAt = start, EndAt = end };

    [Fact]
    public void Late_UsesPolicyThreshold()
    {
        AttendancePeriodCalculator.Classify(Data(Record(At(9, 10), At(17, 0)), threshold: 15)).Late.Should().Be(0);
        AttendancePeriodCalculator.Classify(Data(Record(At(9, 15), At(17, 0)), threshold: 15)).Late.Should().Be(1);
    }

    [Fact]
    public void Late_ZeroThresholdKeepsAnyMinute()
        => AttendancePeriodCalculator.Classify(Data(Record(At(9, 1), At(17, 0)))).Late.Should().Be(1);

    [Fact]
    public void Early_UsesPolicyThreshold()
    {
        AttendancePeriodCalculator.Classify(Data(Record(At(9, 0), At(16, 50)), threshold: 15)).EarlyDepartures.Should().Be(0);
        AttendancePeriodCalculator.Classify(Data(Record(At(9, 0), At(16, 40)), threshold: 15)).EarlyDepartures.Should().Be(1);
    }

    [Fact]
    public void Late_MeasuredFromEndOfHourlyLeaveCoveringStart()
    {
        var record = Record(At(11, 20), At(17, 0));
        var data = Data(record, 0, Leave(At(9, 0), At(11, 0)));

        AttendancePeriodCalculator.MissingMinutes(record, data).Should().Be((20, 0));
    }

    [Fact]
    public void Early_MeasuredToStartOfLeaveCoveringEnd()
    {
        var record = Record(At(9, 0), At(14, 30));
        var data = Data(record, 0, Leave(At(15, 0), At(17, 0)));

        AttendancePeriodCalculator.MissingMinutes(record, data).Should().Be((0, 30));
    }

    [Fact]
    public void HourlyLeave_ExactlyCoveringTheGap_IsNotLate()
    {
        var record = Record(At(11, 0), At(17, 0));

        AttendancePeriodCalculator.Classify(Data(record, 0, Leave(At(9, 0), At(11, 0)))).Late.Should().Be(0);
    }

    [Fact]
    public void FullDayLeave_NoLateOrEarly()
    {
        var record = Record(At(12, 0), At(13, 0));
        var data = Data(record, 0, Leave(At(0, 0), At(23, 59)));

        AttendancePeriodCalculator.MissingMinutes(record, data).Should().Be((0, 0));
        var c = AttendancePeriodCalculator.Classify(data);
        (c.Late, c.EarlyDepartures).Should().Be((0, 0));
    }

    [Fact]
    public void Classify_NoLongerReportsShortHours()
    {
        var c = AttendancePeriodCalculator.Classify(Data(Record(At(9, 0), At(17, 0), required: 480, worked: 300)));

        c.ShortHours.Should().Be(0);
        c.Dates.ShortHours.Should().BeEmpty();
    }

    [Fact]
    public void OvernightShift_LateAndEarlyStillMeasured()
    {
        var record = new AttendanceRecord
        {
            Id = Guid.NewGuid(), Date = Day, ExpectedWorkingDay = true,
            ScheduledStart = new TimeOnly(22, 0), ScheduledEnd = new TimeOnly(6, 0),
            ActualStart = At(22, 30), ActualEnd = At(22, 30).AddHours(6.5), // 22:30 -> 05:00 next day
        };

        AttendancePeriodCalculator.MissingMinutes(record, Data(record)).Should().Be((30, 60));
    }
}
