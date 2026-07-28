using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.RespondAgentCommand;

public sealed record RespondAgentCommand(
    Guid AgentId,
    Guid CommandId,
    string Decision,
    string ConsentNoticeVersion)
    : IRequest<Result<AgentCommandDecisionResponse>>;

public sealed record AgentCommandDecisionResponse(
    Guid CommandId,
    string Status,
    DateTimeOffset DecisionAt,
    DateTimeOffset? CaptureExpiresAt);

