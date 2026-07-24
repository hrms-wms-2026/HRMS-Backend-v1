using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentPolicy;

public record GetAgentPolicyQuery(Guid AgentId) : IRequest<Result<string>>;
