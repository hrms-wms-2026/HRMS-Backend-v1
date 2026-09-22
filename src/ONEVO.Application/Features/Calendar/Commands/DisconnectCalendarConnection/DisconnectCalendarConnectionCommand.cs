using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.DisconnectCalendarConnection;

public sealed record DisconnectCalendarConnectionCommand(Guid Id) : IRequest<Result<Unit>>;
