using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Queries.GetBiometricProfile;

public record GetBiometricProfileQuery : IRequest<Result<BiometricProfileResponseDto>>;
