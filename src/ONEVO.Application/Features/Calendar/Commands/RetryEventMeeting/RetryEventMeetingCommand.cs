using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

namespace ONEVO.Application.Features.Calendar.Commands.RetryEventMeeting;

/// <summary>The organizer's Retry on a provisioning_unknown meeting: same generation, adopt-first.</summary>
public sealed record RetryEventMeetingCommand(Guid EventId) : IRequest<Result<CreateEventMeetingResult>>;
