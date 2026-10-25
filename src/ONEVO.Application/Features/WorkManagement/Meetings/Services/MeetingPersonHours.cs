using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// The hours an ended occurrence uses (2026-10-26 design, Part A): its duration times the organizer plus every internal
/// attendee who did not decline, each for the full duration whatever their attendance. Guests never count; a
/// cancelled occurrence is zero.
/// </summary>
public static class MeetingPersonHours
{
    public static decimal Compute(int durationMinutes, Guid organizerEmployeeId, IReadOnlyList<EffectiveAttendee> attendees,
        bool cancelled)
    {
        if (cancelled || durationMinutes <= 0)
            return 0m;

        var people = attendees
            .Where(a => a.Kind == MeetingAttendeeKinds.Employee && a.EmployeeId is not null
                && a.Response != CalendarEventParticipantStatuses.Rejected)
            .Select(a => a.EmployeeId!.Value)
            .Append(organizerEmployeeId)
            .Distinct()
            .Count();
        return Math.Round(durationMinutes / 60m * people, 2, MidpointRounding.AwayFromZero);
    }
}
