using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;

public sealed record CancelRecurringOccurrenceCommand(Guid MasterId, DateTimeOffset OriginalStart) : IRequest<Result>;
