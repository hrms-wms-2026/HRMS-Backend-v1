using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>
/// Meetings-as-tasks: once Meetings:Create is on, a meeting with people or an online link belongs to a project (a Meeting
/// work item, Office for non-project meetings), never to a bare calendar event. A new calendar event cannot carry
/// participants, guests or a link, and a personal event (none of them yet) cannot gain them. Events that already had
/// attendees or a link before keep today's behaviour (spec D15).
/// </summary>
public sealed class CalendarMeetingGuard(IOptions<MeetingsOptions> options, ICalendarEventRepository events)
{
    public const string ErrorCode = "calendar_meeting_requires_work_item";

    public bool Active => options.Value.Create;

    public static Result<T> Refusal<T>() => Result<T>.Failure(
        "A meeting with people or an online link belongs to a project: create it as a Meeting (work item).", 400, ErrorCode);

    /// <summary>True for a new event that would be a meeting (people, guests, a link or a pending Teams/Zoom link).</summary>
    public bool RefusesNewEvent(IReadOnlyCollection<Guid> participants, IReadOnlyCollection<string>? guests, string? meetingLink,
        string? pendingMeetingProvider)
        => Active && (participants.Count > 0 || guests is { Count: > 0 } || !string.IsNullOrWhiteSpace(meetingLink)
            || !string.IsNullOrWhiteSpace(pendingMeetingProvider));

    /// <summary>True when the event has no participants, guests or link yet, so adding one would turn it into a meeting.</summary>
    public async Task<bool> RefusesBecomingMeetingAsync(Guid tenantId, CalendarEvent calendarEvent, CancellationToken ct)
    {
        if (!Active || !string.IsNullOrWhiteSpace(calendarEvent.MeetingLink))
            return false;
        var participants = await events.GetParticipantsForEventsAsync(tenantId, [calendarEvent.Id], ct);
        if (participants?.GetValueOrDefault(calendarEvent.Id) is { Count: > 0 } current && current.Any(p => !p.IsDeleted))
            return false;
        var guests = await events.GetGuestsForEventsAsync(tenantId, [calendarEvent.Id], ct);
        return guests?.GetValueOrDefault(calendarEvent.Id) is not { Count: > 0 };
    }
}
