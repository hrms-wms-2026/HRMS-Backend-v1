using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.StartCalendarConnection;

public sealed record StartCalendarConnectionResponse(string AuthorizeUrl);

public sealed record StartCalendarConnectionCommand(string Provider) : IRequest<Result<StartCalendarConnectionResponse>>;
