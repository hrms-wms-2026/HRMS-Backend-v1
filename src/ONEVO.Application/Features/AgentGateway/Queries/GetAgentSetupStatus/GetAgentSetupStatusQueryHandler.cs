using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Queries.GetAgentSetupStatus;

public sealed class GetAgentSetupStatusQueryHandler
    : IRequestHandler<GetAgentSetupStatusQuery, Result<AgentSetupStatusDto>>
{
    private readonly IAgentGatewayRepository _agents;
    private readonly IUserProfileRepository _profiles;
    private readonly IVerificationRepository _verification;

    public GetAgentSetupStatusQueryHandler(
        IAgentGatewayRepository agents,
        IUserProfileRepository profiles,
        IVerificationRepository verification)
    {
        _agents = agents;
        _profiles = profiles;
        _verification = verification;
    }

    public async Task<Result<AgentSetupStatusDto>> Handle(
        GetAgentSetupStatusQuery request,
        CancellationToken cancellationToken)
    {
        var agent = await _agents.GetAgentByIdAsync(request.AgentId, cancellationToken);
        if (agent is null)
            return Result<AgentSetupStatusDto>.NotFound("Agent not found.");

        if (agent.Status != "active" || agent.EmployeeId is null)
            return Result<AgentSetupStatusDto>.Forbidden("Agent is not an approved active device.");

        var employee = await _profiles.GetEmployeeByIdAsync(
            agent.EmployeeId.Value,
            cancellationToken);
        if (employee is null || employee.TenantId != agent.TenantId)
            return Result<AgentSetupStatusDto>.NotFound("Employee not found.");

        var settings = await _profiles.GetWorkLocationSettingsAsync(
            employee.Id,
            cancellationToken);
        var policy = await _verification.GetActivePolicyAsync(cancellationToken);
        var reference = await _verification.GetActiveReferencePhotoAsync(
            employee.Id,
            cancellationToken);
        var remoteProfile = await _verification.GetActiveRemoteProfileAsync(
            employee.Id,
            cancellationToken);
        var latestEvidence = await _agents.GetLatestWorkLocationEvidenceAsync(
            agent.Id,
            cancellationToken);

        var workMode = NormalizeWorkMode(settings?.WorkMode);
        var locationRequired = settings?.WorkLocationVerificationEnabled ?? true;
        var locationReady = !locationRequired ||
            (workMode == "remote"
                ? remoteProfile is not null
                : latestEvidence is not null &&
                  latestEvidence.MatchStatus is "matched" or "not_evaluated");

        var referenceRequired = policy is { IsActive: true } &&
            (policy.RequirePhotoClockIn ||
             policy.RequirePhotoClockOut ||
             policy.BlockMonitoringUntilReferenceApproved);
        var referenceReady = !referenceRequired || reference is not null;

        var setupState = !locationReady
            ? "location_required"
            : !referenceReady
                ? "reference_required"
                : "ready";

        return Result<AgentSetupStatusDto>.Success(new AgentSetupStatusDto(
            workMode,
            locationRequired,
            locationReady,
            referenceRequired,
            referenceReady,
            remoteProfile?.Status,
            setupState));
    }

    private static string NormalizeWorkMode(string? workMode) =>
        workMode?.Trim().ToLowerInvariant() switch
        {
            "remote" => "remote",
            "field" => "field",
            "hybrid" => "hybrid",
            "on_site" or "onsite" => "onsite",
            _ => "onsite"
        };
}
