using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class AttendanceSyncPolicyTests
{
    private static readonly DateTimeOffset EventEnd = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(48)]
    public void Final_IsSynced_AtAnyAge(int hoursAfterEnd)
        => Assert.Equal(CalendarAttendanceSyncStatuses.Synced,
            AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.Final, EventEnd, EventEnd.AddHours(hoursAfterEnd)));

    [Fact]
    public void NotReady_BeforeTheCutOff_RetriesLater()
        => Assert.Null(AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.NotReady, EventEnd, EventEnd.AddHours(23).AddMinutes(59)));

    [Fact]
    public void NotReady_AtTheCutOff_IsUnavailable()
        => Assert.Equal(CalendarAttendanceSyncStatuses.Unavailable,
            AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.NotReady, EventEnd, EventEnd.AddHours(24)));

    [Fact]
    public void Failed_BeforeTheCutOff_RetriesLater()
        => Assert.Null(AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.Failed, EventEnd, EventEnd.AddHours(2)));

    [Fact]
    public void Failed_AfterTheCutOff_IsSyncFailed_NotUnavailable()
        => Assert.Equal(CalendarAttendanceSyncStatuses.Failed,
            AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.Failed, EventEnd, EventEnd.AddHours(30)));
}
