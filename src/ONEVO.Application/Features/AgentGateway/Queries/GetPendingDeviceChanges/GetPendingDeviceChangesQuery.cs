using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetPendingDeviceChanges;

public sealed record PendingDeviceChangeDto(
    Guid RequestId,
    Guid EmployeeId,
    Guid CurrentAgentId,
    Guid RequestedAgentId,
    string Status,
    string? Reason,
    DateTimeOffset RequestedAt);

public sealed record GetPendingDeviceChangesQuery(int Page, int PageSize)
    : IRequest<Result<IReadOnlyList<PendingDeviceChangeDto>>>;
