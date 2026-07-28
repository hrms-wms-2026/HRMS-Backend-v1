using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.DTOs;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentWorkContext;

public sealed record GetAgentWorkContextQuery(Guid AgentId)
    : IRequest<Result<AgentWorkContextDto>>;
