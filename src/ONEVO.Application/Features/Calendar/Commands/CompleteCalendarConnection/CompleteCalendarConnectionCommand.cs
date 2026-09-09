using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;

public sealed record CompleteCalendarConnectionCommand(string Provider, string Code, string State) : IRequest<Result<string>>;
