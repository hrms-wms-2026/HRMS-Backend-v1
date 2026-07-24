using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.UpdateHeartbeat;

public class UpdateHeartbeatCommandHandler : IRequestHandler<UpdateHeartbeatCommand, Result>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;

    public UpdateHeartbeatCommandHandler(IAgentGatewayRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result> Handle(UpdateHeartbeatCommand request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await _repo.AddHealthLogAsync(new AgentHealthLog
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            AgentId = request.AgentId,
            ReportedAt = now,
            CpuUsage = request.CpuUsage,
            MemoryMb = request.MemoryMb,
            ErrorsJson = "[]",
            TamperDetected = false
        }, cancellationToken);

        await _repo.TouchHeartbeatAsync(request.AgentId, now, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
