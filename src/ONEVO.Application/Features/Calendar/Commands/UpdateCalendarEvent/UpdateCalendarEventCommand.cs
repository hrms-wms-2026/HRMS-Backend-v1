using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;

public sealed record UpdateCalendarEventCommand(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsAllDay,
    string? Location,
    string? MeetingLink,
    string? Color,
    string Recurrence) : IRequest<Result<CalendarEventItem>>;
