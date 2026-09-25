using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

public sealed record CreateEventMeetingResult(string JoinUrl);

/// <summary>Provider is a ONEVO.Domain.Features.Calendar.Entities.CalendarEventMeetingProviders
/// value ("microsoft_teams" | "zoom").</summary>
public sealed record CreateEventMeetingCommand(Guid EventId, string Provider) : IRequest<Result<CreateEventMeetingResult>>;
