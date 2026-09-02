using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceTodayStateServiceCalculationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OpenSessionWithinThresholdReturnsLiveElapsedMinutes()
    {
        var record = new AttendanceRecord
        {
            ActualStart = Now.AddHours(-2),
            ActualEnd = null,
            WorkedMinutes = 0
        };

        var result = AttendanceTodayStateService.CalculateWorkedMinutes(record, breakUsedMinutes: 10, Now);

        result.Should().Be(110);
    }

    [Fact]
    public void OpenSessionPastMissingClockOutThresholdReturnsPersistedMinutesNotLiveElapsed()
    {
        var record = new AttendanceRecord
        {
            ActualStart = Now.AddHours(-20),
            ActualEnd = null,
            WorkedMinutes = 0
        };

        var result = AttendanceTodayStateService.CalculateWorkedMinutes(record, breakUsedMinutes: 0, Now);

        result.Should().Be(0);
        result.Should().NotBe((int)TimeSpan.FromHours(20).TotalMinutes);
    }
}
