using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ExchangeActivationCode;

public record ExchangeActivationCodeCommand(
    string Code,
    string DeviceName,
    string DeviceOs,
    string DeviceFingerprint) : IRequest<Result<TrayAuthResponseDto>>;
