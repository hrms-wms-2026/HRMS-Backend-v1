using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.Location;

namespace ONEVO.Application.Features.AgentGateway.Commands.CaptureSetupLocation;

public sealed record CaptureSetupLocationCommand(
    Guid AgentId,
    LocationCapture Capture,
    string? LocalNetworkClass,
    string? WifiBssidHash,
    string? GatewayMacHash,
    bool VpnDetected) : IRequest<Result<CaptureSetupLocationResult>>;

public sealed record CaptureSetupLocationResult(
    Guid EvidenceId,
    string MatchState,
    string? RemoteProfileState,
    string? FailureCode,
    decimal? DistanceMeters);
