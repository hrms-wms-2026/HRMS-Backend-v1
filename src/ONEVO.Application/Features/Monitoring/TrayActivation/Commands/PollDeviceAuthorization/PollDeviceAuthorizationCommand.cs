using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.PollDeviceAuthorization;

public sealed record PollDeviceAuthorizationCommand(
    string DeviceCode,
    string DeviceFingerprint) : IRequest<Result<TrayAuthResponseDto>>;
