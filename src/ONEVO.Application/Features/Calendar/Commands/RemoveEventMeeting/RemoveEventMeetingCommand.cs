using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;

public sealed record RemoveEventMeetingCommand(Guid EventId) : IRequest<Result>;
