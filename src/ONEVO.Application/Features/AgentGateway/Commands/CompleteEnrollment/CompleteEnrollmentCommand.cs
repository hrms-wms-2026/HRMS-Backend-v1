using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.DTOs;

namespace ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;

public record CompleteEnrollmentCommand(
    Guid EnrollmentId,
    string DeviceId,
    string AuthorizationCode
) : IRequest<Result<EnrollCompleteResponseDto>>;
