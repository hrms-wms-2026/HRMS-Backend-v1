using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;

public record CompleteEnrollmentAttemptCommand(Guid AttemptId) : IRequest<Result<BiometricProfileResponseDto>>;
