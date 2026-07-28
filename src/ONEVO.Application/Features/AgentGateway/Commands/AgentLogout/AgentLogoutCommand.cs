using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.AgentLogout;

/// <summary>Agent ends its own active employee-device session.</summary>
public record AgentLogoutCommand(Guid AgentId) : IRequest<Result>;
