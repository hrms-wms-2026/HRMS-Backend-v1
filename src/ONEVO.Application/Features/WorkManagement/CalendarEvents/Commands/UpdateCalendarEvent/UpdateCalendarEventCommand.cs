using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.UpdateCalendarEvent;

public sealed record UpdateCalendarEventCommand(
    Guid Id,
    string? Name,
    string? Color,
    IReadOnlyList<Guid>? ObjectiveIds)
    : IRequest<Result<CalendarEventResponse>>;
