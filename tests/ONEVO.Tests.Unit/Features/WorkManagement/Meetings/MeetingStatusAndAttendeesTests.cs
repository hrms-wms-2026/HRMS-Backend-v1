using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Derived status (spec D10) and an occurrence's attendees = series − removes + adds (spec 4.2).</summary>
public sealed class MeetingStatusAndAttendeesTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static (MeetingDetails, MeetingSeries) Meeting(DateTimeOffset? occurrenceCancelled = null, DateTimeOffset? seriesCancelled = null)
        => (new MeetingDetails { StartAt = Start, EndAt = Start.AddMinutes(30), CancelledAt = occurrenceCancelled },
            new MeetingSeries { CancelledAt = seriesCancelled });

    [Theory]
    [InlineData(-60, MeetingStatuses.Scheduled)]
    [InlineData(0, MeetingStatuses.InProgress)]
    [InlineData(29, MeetingStatuses.InProgress)]
    [InlineData(30, MeetingStatuses.Completed)]
    public void Status_FollowsTheClock(int minutesFromStart, string expected)
    {
        var (details, series) = Meeting();

        Assert.Equal(expected, MeetingStatusCalculator.Of(details, series, Start.AddMinutes(minutesFromStart)));
    }

    [Fact]
    public void Cancellation_WinsOverTime_OnEitherSide()
    {
        var (cancelledOccurrence, liveSeries) = Meeting(occurrenceCancelled: Start.AddDays(-1));
        var (liveOccurrence, cancelledSeries) = Meeting(seriesCancelled: Start.AddDays(-1));

        Assert.Equal(MeetingStatuses.Cancelled, MeetingStatusCalculator.Of(cancelledOccurrence, liveSeries, Start.AddHours(2)));
        Assert.Equal(MeetingStatuses.Cancelled, MeetingStatusCalculator.Of(liveOccurrence, cancelledSeries, Start.AddMinutes(-10)));
    }

    [Fact]
    public void ASeriesCancellation_LeavesOccurrencesThatHadAlreadyStarted()
    {
        // Final-review I3: cancelling "all" after ten weeks never rewrites the ten meetings that happened.
        var (past, series) = Meeting(seriesCancelled: Start.AddDays(3));
        var (inProgress, _) = Meeting(seriesCancelled: Start.AddMinutes(10));

        Assert.Equal(MeetingStatuses.Completed, MeetingStatusCalculator.Of(past, series, Start.AddDays(4)));
        Assert.Equal(MeetingStatuses.Completed, MeetingStatusCalculator.Of(inProgress, new MeetingSeries { CancelledAt = Start.AddMinutes(10) }, Start.AddHours(1)));
    }

    [Fact]
    public void Attendees_AreTheSeriesMinusRemovesPlusAdds()
    {
        Guid alice = Guid.NewGuid(), bob = Guid.NewGuid(), carol = Guid.NewGuid(), gone = Guid.NewGuid();
        var participants = new[]
        {
            new CalendarEventParticipant { EmployeeId = alice, ResponseStatus = CalendarEventParticipantStatuses.Accepted },
            new CalendarEventParticipant { EmployeeId = bob },
            new CalendarEventParticipant { EmployeeId = gone, IsDeleted = true },
        };
        var guests = new[] { new CalendarEventGuest { Email = "Client@Example.com" } };
        var overrides = new[]
        {
            new MeetingOccurrenceAttendee { AttendeeKind = MeetingAttendeeKinds.Employee, EmployeeId = bob, Change = MeetingAttendeeChanges.Remove },
            new MeetingOccurrenceAttendee { AttendeeKind = MeetingAttendeeKinds.Employee, EmployeeId = carol, Change = MeetingAttendeeChanges.Add, ResponseStatus = "pending" },
            new MeetingOccurrenceAttendee { AttendeeKind = MeetingAttendeeKinds.Guest, Email = "client@example.com", Change = MeetingAttendeeChanges.Remove },
            new MeetingOccurrenceAttendee { AttendeeKind = MeetingAttendeeKinds.Guest, Email = "vendor@example.com", Change = MeetingAttendeeChanges.Add },
        };

        var attendees = EffectiveAttendees.Compute(participants, guests, overrides);

        Assert.Equal(
            [("employee", alice, null, "series"), ("employee", carol, null, "occurrence"), ("guest", (Guid?)null, "vendor@example.com", "occurrence")],
            attendees.Select(a => (a.Kind, a.EmployeeId, a.Email, a.Source)).ToList());
        Assert.Equal(CalendarEventParticipantStatuses.Accepted, attendees[0].Response);
    }

    [Fact]
    public void AnAddForSomeoneAlreadyInTheSeries_KeepsTheSeriesEntry()
    {
        var alice = Guid.NewGuid();
        var attendees = EffectiveAttendees.Compute(
            [new CalendarEventParticipant { EmployeeId = alice }], [],
            [new MeetingOccurrenceAttendee { AttendeeKind = MeetingAttendeeKinds.Employee, EmployeeId = alice, Change = MeetingAttendeeChanges.Add }]);

        Assert.Equal("series", Assert.Single(attendees).Source);
    }
}
