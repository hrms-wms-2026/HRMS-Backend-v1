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
    string? Timezone,
    string? Location,
    string? MeetingLink,
    string? Color,
    string Recurrence,
    IReadOnlyList<Guid> ParticipantEmployeeIds) : IRequest<Result<CalendarEventItem>>;
