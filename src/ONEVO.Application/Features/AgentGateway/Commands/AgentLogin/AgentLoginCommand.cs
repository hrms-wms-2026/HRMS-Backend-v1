using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.DTOs;

namespace ONEVO.Application.Features.AgentGateway.Commands.AgentLogin;

/// <summary>
/// Resume or refresh an employee-device session on an already-enrolled agent.
/// Auth: AgentPolicy (Device JWT required).
/// </summary>
public record AgentLoginCommand(Guid AgentId) : IRequest<Result<AgentLoginResponseDto>>;
