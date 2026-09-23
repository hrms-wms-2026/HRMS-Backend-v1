using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

public sealed record CreateEventMeetingResult(string JoinUrl);

public sealed record CreateEventMeetingCommand(Guid EventId) : IRequest<Result<CreateEventMeetingResult>>;
