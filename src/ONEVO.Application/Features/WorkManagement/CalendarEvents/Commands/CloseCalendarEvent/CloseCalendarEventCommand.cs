using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CloseCalendarEvent;

public sealed record CloseCalendarEventCommand(Guid Id) : IRequest<Result<CalendarEventResponse>>;
