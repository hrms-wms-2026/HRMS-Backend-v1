using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;

public sealed record DeleteCalendarEventCommand(Guid Id) : IRequest<Result>;
