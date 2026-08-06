using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RefreshTrayToken;

public record RefreshTrayTokenCommand(
    string RefreshToken,
    string DeviceFingerprint) : IRequest<Result<TrayAuthResponseDto>>;
