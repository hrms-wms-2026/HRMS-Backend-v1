using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetCurrentPresence;

public sealed record GetCurrentPresenceQuery(Guid AgentId)
    : IRequest<Result<CurrentPresenceDto>>;

public sealed record CurrentPresenceDto(
    string Status,
    string MonitoringState,
    DateTimeOffset? ClockedInAt,
    Guid? BreakId,
    DateTimeOffset? BreakStartedAt,
    DateTimeOffset? MonitoringHardStopAt);

