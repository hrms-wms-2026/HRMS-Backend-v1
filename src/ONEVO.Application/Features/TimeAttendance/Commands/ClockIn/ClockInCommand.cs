using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.Location;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;

public sealed record ClockInCommand(
    Guid AgentId,
    string IdempotencyKey,
    LocationCapture? Capture,
    string? LocalNetworkClass,
    string? WifiBssidHash,
    string? GatewayMacHash,
    bool VpnDetected,
    Guid? VerificationRecordId)
    : IRequest<Result<ClockInResponse>>;

