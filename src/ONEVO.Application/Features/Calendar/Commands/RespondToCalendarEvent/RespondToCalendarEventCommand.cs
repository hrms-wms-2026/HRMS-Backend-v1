using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;

public sealed record RespondToCalendarEventCommand(
    Guid EventId, string ResponseStatus, string? Reason = null, Guid? NomineeEmployeeId = null) : IRequest<Result>;
