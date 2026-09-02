using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;

public sealed record RespondToCalendarEventCommand(Guid EventId, string ResponseStatus) : IRequest<Result>;
