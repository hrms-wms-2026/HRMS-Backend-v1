using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.UpdateHeartbeat;

public record UpdateHeartbeatCommand(
    Guid AgentId,
    Guid TenantId,
    decimal CpuUsage,
    int MemoryMb,
    string MonitoringState) : IRequest<Result>;
