using ONEVO.Application.Features.Calendar.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

/// <summary>Calendar P0-2 (plan C0): an empty attendance report is retried until the cutoff, then marked unavailable.</summary>
public sealed class CalendarAttendanceSyncPolicyTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2, 1, CalendarAttendanceSyncOutcome.Synced)]
    [InlineData(0, 1, CalendarAttendanceSyncOutcome.RetryLater)]
    [InlineData(0, 23, CalendarAttendanceSyncOutcome.RetryLater)]
    [InlineData(0, 24, CalendarAttendanceSyncOutcome.Unavailable)]
    [InlineData(0, 30, CalendarAttendanceSyncOutcome.Unavailable)]
    [InlineData(1, 30, CalendarAttendanceSyncOutcome.Synced)]
    public void Decide(int records, int hoursAfterEnd, CalendarAttendanceSyncOutcome expected)
        => Assert.Equal(expected, CalendarAttendanceSyncPolicy.Decide(records, End, End.AddHours(hoursAfterEnd)));

    [Fact]
    public void An_unknown_event_end_keeps_retrying()
        => Assert.Equal(CalendarAttendanceSyncOutcome.RetryLater, CalendarAttendanceSyncPolicy.Decide(0, null, End.AddDays(30)));
}
