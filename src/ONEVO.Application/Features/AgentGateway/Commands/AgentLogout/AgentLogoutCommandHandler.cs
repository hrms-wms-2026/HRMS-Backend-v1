using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Commands.AgentLogout;

public class AgentLogoutCommandHandler : IRequestHandler<AgentLogoutCommand, Result>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;

    public AgentLogoutCommandHandler(IAgentGatewayRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result> Handle(AgentLogoutCommand request, CancellationToken cancellationToken)
    {
        var agent = await _repo.GetAgentByIdAsync(
            request.AgentId,
            cancellationToken);
        if (agent is null)
            return Result.NotFound("Agent not found.");

        await _repo.EndActiveSessionAsync(
            agent.DeviceId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
