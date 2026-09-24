using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;

public sealed record EditRecurringOccurrenceCommand(
    Guid MasterId,
    DateTimeOffset OriginalStart,
    RecurrenceEditScope Scope,
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsAllDay,
    string? Location,
    string? MeetingLink,
    string? Color) : IRequest<Result<CalendarEventItem>>;
