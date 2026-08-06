using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.GenerateActivationCode;

public record GenerateActivationCodeCommand : IRequest<Result<ActivationCodeResponseDto>>;
