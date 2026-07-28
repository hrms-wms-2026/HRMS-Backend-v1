using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetPendingAgentCommands;

public sealed record GetPendingAgentCommandsQuery(
    Guid AgentId,
    int Limit) : IRequest<Result<IReadOnlyList<AgentCommandDto>>>;

public sealed record AgentCommandDto(
    Guid Id,
    string Type,
    JsonElement Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    uint Version);

