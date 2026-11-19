using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeeting;

/// <summary><see cref="Status"/> is "none" when the event has no online meeting row.</summary>
public sealed record EventMeetingStatusResult(string Status, string? Provider, string? JoinUrl, string? FailureReason);

public sealed record GetEventMeetingQuery(Guid EventId) : IRequest<Result<EventMeetingStatusResult>>;
