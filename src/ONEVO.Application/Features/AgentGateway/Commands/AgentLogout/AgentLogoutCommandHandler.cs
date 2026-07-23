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
        await _repo.EndActiveSessionAsync(request.DeviceId, DateTimeOffset.UtcNow, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
