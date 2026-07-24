using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.IngestBatch;

public class IngestBatchCommandHandler : IRequestHandler<IngestBatchCommand, Result>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;

    public IngestBatchCommandHandler(IAgentGatewayRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result> Handle(IngestBatchCommand request, CancellationToken cancellationToken)
    {
        var agent = await _repo.GetAgentByIdAsync(request.AgentId, cancellationToken);
        if (agent is null || agent.Status == "revoked")
            return Result.Failure("Agent not found or revoked.", 401);

        await _repo.AddRawActivityBatchAsync(new ActivityRawBuffer
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            AgentDeviceId = request.AgentId,
            ReceivedAt = DateTimeOffset.UtcNow,
            PayloadJson = request.PayloadJson
        }, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
