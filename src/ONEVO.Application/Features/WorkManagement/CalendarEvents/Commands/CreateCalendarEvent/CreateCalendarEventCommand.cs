using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;

public sealed record CreateCalendarEventCommand(
    Guid ProjectId,
    string Name,
    string Color,
    IReadOnlyList<Guid> ObjectiveIds)
    : IRequest<Result<CalendarEventResponse>>;
