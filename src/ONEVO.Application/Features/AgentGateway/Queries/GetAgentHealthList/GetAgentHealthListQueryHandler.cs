using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentHealthList;

public class GetAgentHealthListQueryHandler
    : IRequestHandler<GetAgentHealthListQuery, Result<List<AgentHealthListItemDto>>>
{
    private readonly IAgentGatewayRepository _repo;
    public GetAgentHealthListQueryHandler(IAgentGatewayRepository repo) => _repo = repo;

    public async Task<Result<List<AgentHealthListItemDto>>> Handle(
        GetAgentHealthListQuery request, CancellationToken ct)
    {
        var agents = await _repo.GetActiveAgentsAsync(ct);
        var dtos = agents.Select(a => new AgentHealthListItemDto(
            a.Id, a.DeviceName, a.Status, a.AgentVersion, a.LastHeartbeatAt, a.EmployeeId)).ToList();
        return Result<List<AgentHealthListItemDto>>.Success(dtos);
    }
}
