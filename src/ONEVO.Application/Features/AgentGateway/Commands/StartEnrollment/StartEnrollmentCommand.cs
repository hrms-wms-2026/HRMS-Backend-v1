using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.AgentGateway.DTOs;

namespace ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;

public record StartEnrollmentCommand(
    string DeviceId,
    string DeviceName,
    string OsVersion,
    string AgentVersion,
    string? RedirectUri = null
) : IRequest<Result<EnrollStartResponseDto>>;
