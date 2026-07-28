using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.RecordConsentEvent;

public class RecordConsentEventCommandHandler : IRequestHandler<RecordConsentEventCommand, Result>
{
    private static readonly HashSet<string> ValidDecisions =
        new(StringComparer.Ordinal)
        {
            "allowed",
            "denied",
            "timeout",
            "upload_failed_no_image"
        };

    private readonly IAgentGatewayRepository _agentRepo;
    private readonly IActivityMonitoringRepository _repo;

    public RecordConsentEventCommandHandler(
        IAgentGatewayRepository agentRepo,
        IActivityMonitoringRepository repo)
    {
        _agentRepo = agentRepo;
        _repo = repo;
    }

    public async Task<Result> Handle(
        RecordConsentEventCommand request,
        CancellationToken cancellationToken)
    {
        if (!ValidDecisions.Contains(request.Decision))
            return Result.Failure($"Invalid consent decision '{request.Decision}'.");

        var agent = await _agentRepo.GetAgentByIdAsync(request.AgentDeviceId, cancellationToken);
        if (agent is null || !agent.EmployeeId.HasValue)
            return Result.Failure("Active employee device not found.", 401);

        if (agent.TenantId != request.TenantId)
            return Result.Forbidden("Agent tenant does not match authenticated tenant.");

        var consent = new MonitoringConsentEvent
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            EmployeeId = agent.EmployeeId.Value,
            AgentDeviceId = request.AgentDeviceId,
            IncidentId = request.IncidentId,
            Decision = request.Decision,
            OccurredAt = request.OccurredAt
        };

        await _repo.AddConsentEventAsync(consent, cancellationToken);

        return Result.Success();
    }
}
