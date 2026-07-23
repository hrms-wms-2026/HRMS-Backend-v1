using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.AgentLogin;

public class AgentLoginCommandHandler : IRequestHandler<AgentLoginCommand, Result<AgentLoginResponseDto>>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;

    public AgentLoginCommandHandler(IAgentGatewayRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result<AgentLoginResponseDto>> Handle(
        AgentLoginCommand request, CancellationToken cancellationToken)
    {
        var agent = await _repo.GetAgentByIdAsync(request.AgentId, cancellationToken);
        if (agent is null || agent.Status == "revoked")
            return Result<AgentLoginResponseDto>.Failure("Agent not found or revoked.", 401);

        var now = DateTimeOffset.UtcNow;

        await _repo.EndActiveSessionAsync(agent.DeviceId, now, cancellationToken);
        await _repo.AddSessionAsync(new AgentSession
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            DeviceId = agent.DeviceId,
            EmployeeId = agent.EmployeeId!.Value,
            IsActive = true,
            CreatedAt = now
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var policy = await _repo.GetPolicyByAgentIdAsync(agent.Id, cancellationToken);

        return Result<AgentLoginResponseDto>.Success(new AgentLoginResponseDto(
            EmployeeId: agent.EmployeeId!.Value,
            EmployeeName: string.Empty,
            PolicyJson: policy?.PolicyJson ?? "{}"));
    }
}
