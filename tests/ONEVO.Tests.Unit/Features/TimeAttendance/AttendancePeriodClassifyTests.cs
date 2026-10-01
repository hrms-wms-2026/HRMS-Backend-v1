using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendancePeriodClassifyTests
{
    private static readonly DateOnly From = new(2026, 9, 1), To = new(2026, 9, 30), Today = new(2026, 9, 30);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T12:00:00+00:00");

    private static AttendancePeriodData Data(
        IReadOnlyList<AttendanceRecord>? records = null,
        IReadOnlyList<LeaveRequest>? leaves = null,
        DateOnly? hire = null) =>
        new(records ?? [], TimeZoneInfo.Utc, Now, Today, 60, new Dictionary<DateOnly, int>(), leaves ?? [],
            DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
            ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(),
                From, To, hire ?? new DateOnly(2020, 1, 1), null, Today));

    private static AttendanceRecord Record(DateOnly d, int? requiredMinutes = null, int workedMinutes = 480) => new()
    {
        Id = Guid.NewGuid(), Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 0),
        ActualStart = new DateTimeOffset(d.Year, d.Month, d.Day, 9, 0, 0, TimeSpan.Zero),
        ActualEnd = new DateTimeOffset(d.Year, d.Month, d.Day, 17, 0, 0, TimeSpan.Zero),
        RequiredWorkMinutes = requiredMinutes, WorkedMinutes = workedMinutes
    };

    private static LeaveRequest Leave(DateOnly from, DateOnly to) => new()
    {
        Id = Guid.NewGuid(),
        StartAt = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        EndAt = new DateTimeOffset(to.ToDateTime(new TimeOnly(23, 59)), TimeSpan.Zero)
    };

    [Fact]
    public void NoRecords_EveryPastExpectedDayIsAbsent()
    {
        var c = AttendancePeriodCalculator.Classify(Data());
        c.WorkingDays.Should().Be(22);
        c.Absent.Should().Be(21); // today is not yet absent
        c.Days.Should().HaveCount(30);
        c.Days.Single(d => d.Date == new DateOnly(2026, 9, 30)).Status.Should().Be("none");
        c.Days.Single(d => d.Date == new DateOnly(2026, 9, 5)).Status.Should().Be("off");
    }

    [Fact]
    public void ApprovedLeave_IsLeaveNotAbsent()
    {
        var c = AttendancePeriodCalculator.Classify(Data(leaves: [Leave(new(2026, 9, 8), new(2026, 9, 9))]));
        c.Leave.Should().Be(2);
        c.Absent.Should().Be(19);
    }

    [Fact]
    public void ShortHours_CountedWhenWorkedBelowRequired()
    {
        var c = AttendancePeriodCalculator.Classify(Data(records: [Record(new(2026, 9, 1), requiredMinutes: 480, workedMinutes: 300)]));
        c.ShortHours.Should().Be(1);
        c.Attended.Should().Be(1);
    }

    [Fact]
    public void WeekendWork_CountsAsNonWorkingDayWork()
    {
        var c = AttendancePeriodCalculator.Classify(Data(records: [Record(new(2026, 9, 5))]));
        c.WorkedOnNonWorkingDay.Should().Be(1);
        c.WorkingDays.Should().Be(22);
    }

    [Fact]
    public void WorkDuringLeave_Counted()
    {
        var day = new DateOnly(2026, 9, 2);
        var c = AttendancePeriodCalculator.Classify(Data(records: [Record(day)], leaves: [Leave(day, day)]));
        c.WorkedDuringTimeOff.Should().Be(1);
        c.Leave.Should().Be(0);
    }

    [Fact]
    public void PreHireDays_AreNone()
    {
        var hire = new DateOnly(2026, 9, 21);
        var c = AttendancePeriodCalculator.Classify(Data(hire: hire));
        c.Days.Where(d => d.Date < hire).Should().OnlyContain(d => d.Status == "none");
        c.Absent.Should().Be(7);
    }
}
