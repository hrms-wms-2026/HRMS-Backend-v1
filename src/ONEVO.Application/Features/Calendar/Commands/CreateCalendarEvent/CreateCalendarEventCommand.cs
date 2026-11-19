using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;

public sealed record CreateCalendarEventCommand(
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsAllDay,
    string? Location,
    string? MeetingLink,
    string? Color,
    string Recurrence,
    IReadOnlyList<Guid> ParticipantEmployeeIds,
    string? RecurrenceRule = null,
    IReadOnlyList<string>? GuestEmails = null,
    // Set when the frontend is about to make a separate CreateEventMeetingCommand call right after
    // this one (the "pick Teams/Zoom meeting on create" one-click flow) - lets this handler skip the
    // participant/guest invite email now so NotifyMeetingLinkAddedAsync's later email (with the join
    // link) is their only invite, instead of a link-less one immediately followed by a near-duplicate.
    string? PendingMeetingProvider = null) : IRequest<Result<CalendarEventItem>>;
