using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Location;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Configuration.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.CaptureSetupLocation;

public sealed class CaptureSetupLocationCommandHandler
    : IRequestHandler<CaptureSetupLocationCommand, Result<CaptureSetupLocationResult>>
{
    private const int RemoteProfileRadiusMeters = 250;
    private readonly IAgentGatewayRepository _agents;
    private readonly IUserProfileRepository _profiles;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IVerificationRepository _verification;
    private readonly ILocationVerificationService _locations;
    private readonly IRequestNetworkContext _network;
    private readonly INetworkEvidenceHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public CaptureSetupLocationCommandHandler(
        IAgentGatewayRepository agents,
        IUserProfileRepository profiles,
        ILegalEntityRepository legalEntities,
        IVerificationRepository verification,
        ILocationVerificationService locations,
        IRequestNetworkContext network,
        INetworkEvidenceHasher hasher,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _agents = agents;
        _profiles = profiles;
        _legalEntities = legalEntities;
        _verification = verification;
        _locations = locations;
        _network = network;
        _hasher = hasher;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<CaptureSetupLocationResult>> Handle(
        CaptureSetupLocationCommand request,
        CancellationToken cancellationToken)
    {
        var agent = await _agents.GetAgentByIdAsync(request.AgentId, cancellationToken);
        if (agent is null)
            return Result<CaptureSetupLocationResult>.NotFound("Agent not found.");

        if (agent.Status != "active" || agent.EmployeeId is null)
            return Result<CaptureSetupLocationResult>.Forbidden("Agent is not an approved active device.");

        var employee = await _profiles.GetEmployeeByIdAsync(
            agent.EmployeeId.Value,
            cancellationToken);
        if (employee is null || employee.TenantId != agent.TenantId)
            return Result<CaptureSetupLocationResult>.NotFound("Employee not found.");

        var now = _clock.UtcNow;
        var settings = await _profiles.GetWorkLocationSettingsAsync(employee.Id, cancellationToken);
        var workMode = NormalizeWorkMode(settings?.WorkMode);
        var publicIp = _network.ClientIp;
        if (publicIp is null)
        {
            return Result<CaptureSetupLocationResult>.Failure(
                "request_network_context_unavailable",
                400);
        }

        string? protectedWifi;
        string? protectedGateway;
        try
        {
            protectedWifi = _hasher.Protect(agent.TenantId, request.WifiBssidHash);
            protectedGateway = _hasher.Protect(agent.TenantId, request.GatewayMacHash);
        }
        catch (ArgumentException)
        {
            return Result<CaptureSetupLocationResult>.Failure(
                "invalid_network_evidence_hash",
                400);
        }

        var evidenceId = Guid.NewGuid();
        var locationJson = SerializeCapture(request);
        LocationMatchResult match;
        string? matchedSource = null;
        Guid? matchedSourceId = null;
        string? remoteProfileState = null;

        if (workMode == "remote")
        {
            match = _locations.ValidateCapture(request.Capture, now);
            if (!match.IsValid)
                return InvalidCapture(match);

            var activeProfile = await _verification.GetActiveRemoteProfileAsync(
                employee.Id,
                cancellationToken);
            if (activeProfile is null)
            {
                var profile = await CreateFirstRemoteProfileAsync(
                    agent.TenantId,
                    employee.Id,
                    publicIp,
                    protectedWifi,
                    protectedGateway,
                    request.VpnDetected,
                    locationJson,
                    now,
                    cancellationToken);
                remoteProfileState = profile.Status;
                matchedSource = "remote_profile";
                matchedSourceId = profile.Id;
            }
            else
            {
                var target = TryCreateRemoteTarget(activeProfile);
                if (target is null)
                {
                    return Result<CaptureSetupLocationResult>.Conflict(
                        "Approved remote profile does not contain usable location evidence.");
                }

                match = _locations.Evaluate(request.Capture, target, now);
                if (!match.IsValid)
                    return InvalidCapture(match);

                matchedSource = "remote_profile";
                matchedSourceId = activeProfile.Id;
                remoteProfileState = activeProfile.Status;

                if (!match.IsMatch)
                {
                    remoteProfileState = await CreateRemoteChangeIfNeededAsync(
                        agent.TenantId,
                        employee.Id,
                        activeProfile.Id,
                        publicIp,
                        protectedWifi,
                        protectedGateway,
                        request.VpnDetected,
                        locationJson,
                        now,
                        cancellationToken);
                }
            }
        }
        else if (workMode == "field")
        {
            match = _locations.ValidateCapture(request.Capture, now);
            if (!match.IsValid)
                return InvalidCapture(match);
        }
        else
        {
            if (employee.LegalEntityId is null)
            {
                return Result<CaptureSetupLocationResult>.Conflict(
                    "Employee does not have a Company assignment.");
            }

            var legalEntity = await _legalEntities.GetByIdAsync(
                employee.LegalEntityId.Value,
                cancellationToken);
            if (legalEntity is null ||
                legalEntity.OfficeLatitude is null ||
                legalEntity.OfficeLongitude is null ||
                legalEntity.OfficeAllowedRadiusMeters is null)
            {
                return Result<CaptureSetupLocationResult>.Conflict(
                    "Company office location is not configured.");
            }

            match = _locations.Evaluate(
                request.Capture,
                new LocationTarget(
                    legalEntity.Id,
                    "company_office",
                    legalEntity.OfficeLatitude.Value,
                    legalEntity.OfficeLongitude.Value,
                    legalEntity.OfficeAllowedRadiusMeters.Value),
                now);
            if (!match.IsValid)
                return InvalidCapture(match);

            matchedSource = "company_office";
            matchedSourceId = legalEntity.Id;
        }

        var matchState = workMode == "field"
            ? "not_evaluated"
            : match.IsMatch ? "matched" : "mismatch";
        var confidence = ResolveConfidence(request.Capture, matchState);

        await _agents.AddWorkLocationEvidenceAsync(new AgentWorkLocationEvidence
        {
            Id = evidenceId,
            TenantId = agent.TenantId,
            AgentId = agent.Id,
            EmployeeId = employee.Id,
            CapturedAt = request.Capture.CapturedAt,
            ReceivedAt = now,
            PublicIp = publicIp,
            WifiBssidHash = protectedWifi,
            GatewayMacHash = protectedGateway,
            VpnDetected = request.VpnDetected,
            CoarseLocationJson = locationJson,
            MatchStatus = matchState,
            Confidence = confidence,
            MatchedLocationSource = matchedSource,
            MatchedLocationSourceId = matchedSourceId,
            CreatedAt = now
        }, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return Result<CaptureSetupLocationResult>.Success(new CaptureSetupLocationResult(
            evidenceId,
            matchState,
            remoteProfileState,
            string.IsNullOrEmpty(match.FailureCode) ? null : match.FailureCode,
            match.DistanceMeters));
    }

    private async Task<EmployeeRemoteWorkProfile> CreateFirstRemoteProfileAsync(
        Guid tenantId,
        Guid employeeId,
        System.Net.IPAddress publicIp,
        string? wifiHash,
        string? gatewayHash,
        bool vpnDetected,
        string locationJson,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var policy = await _verification.GetActivePolicyAsync(ct);
        var activeReference = await _verification.GetActiveReferencePhotoAsync(employeeId, ct);
        var referenceBlocks = policy is
        {
            IsActive: true,
            BlockMonitoringUntilReferenceApproved: true
        } && activeReference is null;

        var profile = new EmployeeRemoteWorkProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Status = referenceBlocks ? "pending_capture" : "active",
            CapturedAt = now,
            PublicIp = publicIp,
            WifiBssidHash = wifiHash,
            GatewayMacHash = gatewayHash,
            VpnDetected = vpnDetected,
            CoarseLocationJson = locationJson,
            CreatedAt = now
        };
        await _verification.AddRemoteProfileAsync(profile, ct);
        return profile;
    }

    private async Task<string> CreateRemoteChangeIfNeededAsync(
        Guid tenantId,
        Guid employeeId,
        Guid currentProfileId,
        System.Net.IPAddress publicIp,
        string? wifiHash,
        string? gatewayHash,
        bool vpnDetected,
        string locationJson,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await _verification.GetPendingRemoteChangeAsync(employeeId, ct);
        if (existing is not null)
            return "change_pending";

        var candidate = new EmployeeRemoteWorkProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Status = "pending_capture",
            CapturedAt = now,
            PublicIp = publicIp,
            WifiBssidHash = wifiHash,
            GatewayMacHash = gatewayHash,
            VpnDetected = vpnDetected,
            CoarseLocationJson = locationJson,
            CreatedAt = now
        };
        await _verification.AddRemoteProfileAsync(candidate, ct);
        await _verification.AddRemoteChangeRequestAsync(new RemoteWorkLocationChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            CurrentProfileId = currentProfileId,
            NewProfileId = candidate.Id,
            Reason = "WorkPulse detected a remote work location change.",
            Status = "pending",
            RequestedAt = now
        }, ct);
        return "change_pending";
    }

    private static LocationTarget? TryCreateRemoteTarget(EmployeeRemoteWorkProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.CoarseLocationJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(profile.CoarseLocationJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("latitude", out var latitude) ||
                !root.TryGetProperty("longitude", out var longitude))
            {
                return null;
            }

            return new LocationTarget(
                profile.Id,
                "remote_profile",
                latitude.GetDecimal(),
                longitude.GetDecimal(),
                RemoteProfileRadiusMeters);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SerializeCapture(CaptureSetupLocationCommand request) =>
        JsonSerializer.Serialize(new
        {
            request.Capture.Latitude,
            request.Capture.Longitude,
            request.Capture.AccuracyMeters,
            request.Capture.CapturedAt,
            request.Capture.PermissionState,
            request.LocalNetworkClass
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

    private static Result<CaptureSetupLocationResult> InvalidCapture(
        LocationMatchResult match) =>
        Result<CaptureSetupLocationResult>.Failure(
            $"location_capture_invalid:{match.FailureCode}",
            400);

    private static string ResolveConfidence(LocationCapture capture, string matchState)
    {
        if (matchState == "not_evaluated")
            return "unknown";
        return capture.AccuracyMeters <= 50m ? "high" : "medium";
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
