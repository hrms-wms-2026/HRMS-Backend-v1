using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Requests;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.StartDeviceAuthorization;

public sealed record StartDeviceAuthorizationCommand(
    string DeviceName,
    string DeviceOs,
    string DeviceFingerprint,
    string ClientVersion) : IRequest<Result<StartDeviceAuthorizationResponseDto>>;
