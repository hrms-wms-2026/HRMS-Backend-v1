using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetPendingDeviceChanges;

public sealed class GetPendingDeviceChangesQueryHandler
    : IRequestHandler<
        GetPendingDeviceChangesQuery,
        Result<IReadOnlyList<PendingDeviceChangeDto>>>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private readonly IAgentGatewayRepository _repo;

    public GetPendingDeviceChangesQueryHandler(IAgentGatewayRepository repo) => _repo = repo;

    public async Task<Result<IReadOnlyList<PendingDeviceChangeDto>>> Handle(
        GetPendingDeviceChangesQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = query.PageSize <= 0
            ? DefaultPageSize
            : Math.Min(query.PageSize, MaxPageSize);
        var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);

        var requests = await _repo.GetPendingDeviceChangesAsync(
            skip, pageSize, cancellationToken);
        IReadOnlyList<PendingDeviceChangeDto> response = requests
            .Select(request => new PendingDeviceChangeDto(
                RequestId: request.Id,
                EmployeeId: request.EmployeeId,
                CurrentAgentId: request.CurrentAgentId,
                RequestedAgentId: request.RequestedAgentId,
                Status: request.Status,
                Reason: request.Reason,
                RequestedAt: request.RequestedAt))
            .ToList();

        return Result<IReadOnlyList<PendingDeviceChangeDto>>.Success(response);
    }
}
