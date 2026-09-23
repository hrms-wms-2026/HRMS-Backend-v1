using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.TriggerCalendarSync;

public sealed record TriggerCalendarSyncCommand(Guid Id) : IRequest<Result<Unit>>;
