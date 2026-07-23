using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.AgentLogout;

/// <summary>Agent ends the active employee-device session. Auth: AgentPolicy.</summary>
public record AgentLogoutCommand(string DeviceId) : IRequest<Result>;
