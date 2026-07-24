using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetDeviceChangeStatus;

public sealed class GetDeviceChangeStatusQueryHandler
    : IRequestHandler<GetDeviceChangeStatusQuery, Result<DeviceChangeStatusDto>>
{
    private readonly IAgentGatewayRepository _repo;

    public GetDeviceChangeStatusQueryHandler(IAgentGatewayRepository repo) => _repo = repo;

    public async Task<Result<DeviceChangeStatusDto>> Handle(
        GetDeviceChangeStatusQuery query,
        CancellationToken cancellationToken)
    {
        var agent = await _repo.GetAgentByIdAsync(query.AgentId, cancellationToken);
        if (agent is null)
            return Result<DeviceChangeStatusDto>.NotFound("Agent not found.");

        var request = await _repo.GetDeviceChangeRequestByRequestedAgentIdAsync(
            query.AgentId, cancellationToken);
        var approvalStatus = request?.Status ??
            (string.Equals(agent.Status, "active", StringComparison.Ordinal)
                ? "approved"
                : agent.Status);

        return Result<DeviceChangeStatusDto>.Success(new DeviceChangeStatusDto(
            AgentId: agent.Id,
            DeviceStatus: agent.Status,
            ApprovalStatus: approvalStatus,
            RequestId: request?.Id,
            RequestedAt: request?.RequestedAt,
            ReviewedAt: request?.ReviewedAt,
            ReviewComment: request?.ReviewComment));
    }
}
