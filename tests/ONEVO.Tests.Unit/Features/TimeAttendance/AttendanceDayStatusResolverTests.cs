using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceDayStatusResolverTests
{
    private static readonly DateTimeOffset LocalNow = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ApprovedLeaveWithoutClockInReturnsOnTimeOffWithoutAttention()
    {
        var result = Resolve(new AttendanceSchedule("configured", true, new(9, 0), new(17, 0), 480), null, true);

        result.Status.Should().Be(AttendanceRecord.StatusOnTimeOff);
        result.StatusLabel.Should().Be("On time off");
        result.AttentionType.Should().BeNull();
        result.ShouldHaveClockedIn.Should().BeFalse();
    }

    [Fact]
    public void ApprovedLeaveWithClockInReturnsWorkedDuringTimeOffAttention()
    {
        var result = Resolve(
            new AttendanceSchedule("configured", true, new(9, 0), new(17, 0), 480),
            new AttendanceRecord { ActualStart = LocalNow },
            true);

        result.Status.Should().Be(AttendanceRecord.StatusWorkedDuringTimeOff);
        result.AttentionType.Should().Be("worked_during_time_off");
        result.AttentionSeverity.Should().Be("warning");
    }

    [Fact]
    public void NonWorkingDayWithoutClockInReturnsNonWorkingDayWithoutAttention()
    {
        var result = Resolve(new AttendanceSchedule("configured", false, new(9, 0), new(17, 0), 480), null, false);

        result.Status.Should().Be(AttendanceRecord.StatusNonWorkingDay);
        result.AttentionType.Should().BeNull();
        result.ShouldHaveClockedIn.Should().BeFalse();
    }

    [Fact]
    public void NonWorkingDayWithClockInReturnsWorkedOnNonWorkingDayAttention()
    {
        var result = Resolve(
            new AttendanceSchedule("configured", false, new(9, 0), new(17, 0), 480),
            new AttendanceRecord { ActualStart = LocalNow },
            false);

        result.Status.Should().Be(AttendanceRecord.StatusWorkedOnNonWorkingDay);
        result.AttentionType.Should().Be("worked_on_non_working_day");
    }

    [Fact]
    public void BreakUsageBeyondAllowanceReturnsOverBreakStatusWithoutAutoEnding()
    {
        var record = new AttendanceRecord { ActualStart = LocalNow };
        var result = Resolve(
            new AttendanceSchedule("configured", true, new(9, 0), new(17, 0), 480),
            record,
            false,
            breakAllowance: 30,
            breakUsed: 45);

        result.Status.Should().Be(AttendanceRecord.StatusOverBreak);
        result.BreakOverageMinutes.Should().Be(15);
        result.IsOverBreakAllowance.Should().BeTrue();
        record.ActualEnd.Should().BeNull();
    }

    [Fact]
    public void MissingClockInAfterStartReturnsCriticalAttention()
    {
        var result = Resolve(new AttendanceSchedule("configured", true, new(9, 0), new(17, 0), 480), null, false);

        result.Status.Should().Be(AttendanceRecord.StatusNotClockedIn);
        result.AttentionType.Should().Be("not_clocked_in");
        result.AttentionSeverity.Should().Be("critical");
    }

    [Fact]
    public void OpenBreakWithinAllowanceReturnsOnBreakStatusInsteadOfWorking()
    {
        var record = new AttendanceRecord { ActualStart = LocalNow };
        var result = Resolve(
            new AttendanceSchedule("configured", true, new(9, 0), new(17, 0), 480),
            record,
            false,
            hasOpenBreak: true,
            breakAllowance: 45,
            breakUsed: 5);

        result.Status.Should().Be(AttendanceRecord.StatusOnBreak);
        result.StatusLabel.Should().Be("On break");
        result.IsOverBreakAllowance.Should().BeFalse();
    }

    [Fact]
    public void OpenBreakBeyondAllowanceStillReturnsOverBreakStatus()
    {
        var record = new AttendanceRecord { ActualStart = LocalNow };
        var result = Resolve(
            new AttendanceSchedule("configured", true, new(9, 0), new(17, 0), 480),
            record,
            false,
            hasOpenBreak: true,
            breakAllowance: 30,
            breakUsed: 45);

        result.Status.Should().Be(AttendanceRecord.StatusOverBreak);
    }

    private static AttendanceDayStatusResolution Resolve(
        AttendanceSchedule schedule,
        AttendanceRecord? record,
        bool hasApprovedLeave,
        bool hasOpenBreak = false,
        int? breakAllowance = null,
        int breakUsed = 0)
        => AttendanceDayStatusResolver.Resolve(
            schedule,
            "configured",
            record,
            hasApprovedLeave,
            hasOpenBreak,
            breakAllowance,
            breakUsed,
            LocalNow);
}
