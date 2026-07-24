using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentPolicy;

public class GetAgentPolicyQueryHandler : IRequestHandler<GetAgentPolicyQuery, Result<string>>
{
    private readonly IAgentGatewayRepository _repo;

    public GetAgentPolicyQueryHandler(IAgentGatewayRepository repo) => _repo = repo;

    public async Task<Result<string>> Handle(GetAgentPolicyQuery request, CancellationToken cancellationToken)
    {
        var policy = await _repo.GetPolicyByAgentIdAsync(request.AgentId, cancellationToken);
        if (policy is null)
            return Result<string>.NotFound("Policy not found for agent.");

        return Result<string>.Success(policy.PolicyJson);
    }
}
