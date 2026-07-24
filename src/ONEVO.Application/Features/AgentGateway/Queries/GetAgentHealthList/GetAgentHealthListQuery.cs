using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentHealthList;

public record AgentHealthListItemDto(
    Guid AgentId,
    string DeviceName,
    string Status,
    string AgentVersion,
    DateTimeOffset? LastHeartbeatAt,
    Guid? EmployeeId);

public record GetAgentHealthListQuery : IRequest<Result<List<AgentHealthListItemDto>>>;
