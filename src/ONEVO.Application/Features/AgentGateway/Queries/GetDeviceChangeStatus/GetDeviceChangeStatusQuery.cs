using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetDeviceChangeStatus;

public sealed record DeviceChangeStatusDto(
    Guid AgentId,
    string DeviceStatus,
    string ApprovalStatus,
    Guid? RequestId,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment);

public sealed record GetDeviceChangeStatusQuery(Guid AgentId)
    : IRequest<Result<DeviceChangeStatusDto>>;
