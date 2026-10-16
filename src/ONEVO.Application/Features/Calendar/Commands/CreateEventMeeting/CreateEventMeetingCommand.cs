using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

public sealed record CreateEventMeetingResult(string JoinUrl);

/// <summary>Provider is a ONEVO.Domain.Features.Calendar.Entities.CalendarEventMeetingProviders
/// value ("microsoft_teams" | "zoom"). <see cref="InvitesHeldBack"/> = the event was created with this meeting pending, so its
/// invites were held back for the link (plan C1 P1-5): if the link cannot be created they are sent without it.</summary>
public sealed record CreateEventMeetingCommand(Guid EventId, string Provider, bool InvitesHeldBack = false) : IRequest<Result<CreateEventMeetingResult>>;
