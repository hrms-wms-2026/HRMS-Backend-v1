using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.Policy;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentPolicy;

public record GetAgentPolicyQuery(Guid AgentId) : IRequest<Result<EffectiveAgentPolicy>>;
