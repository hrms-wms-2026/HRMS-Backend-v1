using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Meeting hours (2026-10-26 design, Part A): duration × the organizer and every internal attendee who did
/// not decline; guests never count; a cancelled occurrence is zero.</summary>
public class MeetingPersonHoursTests
{
    private static readonly Guid Organizer = Guid.NewGuid();

    private static EffectiveAttendee Employee(Guid id, string response) =>
        new(MeetingAttendeeKinds.Employee, id, null, response, null, EffectiveAttendees.FromSeries);

    [Fact]
    public void Five_invited_one_declined_one_guest_is_three_hours()
    {
        var attendees = new[]
        {
            Employee(Organizer, CalendarEventParticipantStatuses.Accepted),
            Employee(Guid.NewGuid(), CalendarEventParticipantStatuses.Accepted),
            Employee(Guid.NewGuid(), CalendarEventParticipantStatuses.Pending),
            Employee(Guid.NewGuid(), CalendarEventParticipantStatuses.Rejected),
            new EffectiveAttendee(MeetingAttendeeKinds.Guest, null, "client@example.com", null, null, EffectiveAttendees.FromSeries),
        };

        Assert.Equal(3.00m, MeetingPersonHours.Compute(60, Organizer, attendees, cancelled: false));
    }

    [Fact]
    public void Organizer_always_counts()
    {
        var attendees = new[] { Employee(Organizer, CalendarEventParticipantStatuses.Rejected) };

        Assert.Equal(1.00m, MeetingPersonHours.Compute(60, Organizer, attendees, cancelled: false));
        Assert.Equal(1.00m, MeetingPersonHours.Compute(60, Organizer, [], cancelled: false));
    }

    [Fact]
    public void Occurrence_overrides_change_the_count()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), occurrence = Guid.NewGuid();
        var series = new[]
        {
            new CalendarEventParticipant { EmployeeId = a, ResponseStatus = CalendarEventParticipantStatuses.Accepted },
            new CalendarEventParticipant { EmployeeId = b, ResponseStatus = CalendarEventParticipantStatuses.Accepted },
        };
        var overrides = new[]
        {
            new MeetingOccurrenceAttendee { OccurrenceTaskId = occurrence, EmployeeId = b, Change = MeetingAttendeeChanges.Remove },
            new MeetingOccurrenceAttendee { OccurrenceTaskId = occurrence, EmployeeId = c, Change = MeetingAttendeeChanges.Add,
                ResponseStatus = CalendarEventParticipantStatuses.Pending },
        };

        var attendees = EffectiveAttendees.Compute(series, [], overrides);

        Assert.Equal(3.00m, MeetingPersonHours.Compute(60, Organizer, attendees, cancelled: false));   // a, c, organizer
    }

    [Fact]
    public void Cancelled_is_zero()
    {
        var attendees = new[] { Employee(Organizer, CalendarEventParticipantStatuses.Accepted) };

        Assert.Equal(0m, MeetingPersonHours.Compute(60, Organizer, attendees, cancelled: true));
    }

    [Fact]
    public void Thirty_minutes_two_people_is_one_hour()
    {
        var attendees = new[] { Employee(Guid.NewGuid(), CalendarEventParticipantStatuses.Accepted) };

        Assert.Equal(1.00m, MeetingPersonHours.Compute(30, Organizer, attendees, cancelled: false));
    }

    [Fact]
    public void Odd_minutes_round_to_two_decimals()
    {
        Assert.Equal(0.67m, MeetingPersonHours.Compute(40, Organizer, [], cancelled: false));
    }
}
